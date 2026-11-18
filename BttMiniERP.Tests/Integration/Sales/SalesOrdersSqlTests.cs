using System.Data.Common;
using BttMiniERP.Api.Common;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.DTOs.SalesOrders;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
using BttMiniERP.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
namespace BttMiniERP.Tests.Integration.Sales;

[Collection("MasterData SQL"), Trait("Category", "SqlServer"), Trait("SqlSuite", "Sales")]
public sealed class SalesOrdersSqlTests
{
    private static async Task<(int Customer, int Item)> Setup(MasterDataSqlFixture f)
    {
        await using var db = f.Context();
        var customer = await f.Customers(db).CreateAsync(new() { Code = "CUS", Name = "Customer" }, default);
        var category = await f.Categories(db).CreateAsync(new() { Code = "CAT", Name = "Category" }, default);
        var item = await f.Items(db).CreateAsync(new() { Code = "FG", Name = "Product", CategoryId = category.Id, Unit = "kg", ItemType = ItemType.FinishedProduct }, default);
        return (customer.Id, item.Id);
    }
    private static CreateSalesOrderRequest Create(int customer, int item) => new()
    {
        CustomerId = customer,
        OrderDate = DateTimeOffset.UnixEpoch,
        Lines = [new() { ItemId = item, Quantity = .125m, UnitPrice = .20m }]
    };
    private static Task<List<string>> Stock(ApplicationDbContext db) => IdentityDatabaseVerifier.ReadRowsAsync(db, """
        SELECT CONCAT('B|',ItemId,'|',QuantityOnHand,'|',CreatedAt,'|',CreatedByUserId,'|',UpdatedAt,'|',UpdatedByUserId,'|',CONVERT(varchar(18),RowVersion,1)) FROM InventoryBalances
        UNION ALL SELECT CONCAT('T|',Id,'|',ItemId,'|',QuantityDelta,'|',BalanceAfter,'|',TransactionType,'|',SourceDocumentType,'|',SourceDocumentId,'|',SourceLineId,'|',PerformedByUserId,'|',OccurredAt) FROM InventoryTransactions ORDER BY 1
        """);
    private static async Task Conflict(Func<Task> call) => Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(call)).StatusCode);

    [SalesSqlFact]
    public async Task CreateReplaceLinesCancelAndQueriesNeverChangeStock()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Sales"); var ids = await Setup(f);
        int secondItem;
        await using (var setup = f.Context()) secondItem = (await f.Items(setup).CreateAsync(new() { Code = "FG2", Name = "Product 2", CategoryId = await setup.Categories.Select(x => x.Id).SingleAsync(), Unit = "kg", ItemType = ItemType.FinishedProduct }, default)).Id;
        await using var before = f.Context(); await before.Database.OpenConnectionAsync(); var stock = await Stock(before);
        await using var db = f.Context(); var service = f.Sales(db);
        var request = Create(ids.Customer, ids.Item); request.Lines.Add(new() { ItemId = secondItem, Quantity = 2, UnitPrice = 3 });
        var draft = await service.CreateAsync(request, default);
        Assert.Equal(6.03m, draft.TotalAmount); Assert.Equal(DocumentStatus.Draft, draft.Status); Assert.Equal(f.ActorId, draft.CreatedByUserId);
        Assert.Matches("^SO-[A-F0-9]{32}$", draft.OrderNumber); Assert.Equal(2, draft.Lines.Count);
        Assert.Equal(stock, await Stock(before));
        var empty = new UpdateSalesOrderRequest { CustomerId = ids.Customer, RowVersion = draft.RowVersion, Lines = [] };
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => service.UpdateAsync(draft.Id, empty, default))).StatusCode);
        Assert.Equal(draft.RowVersion, (await service.GetAsync(draft.Id, default)).RowVersion);
        var update = new UpdateSalesOrderRequest
        {
            CustomerId = ids.Customer,
            RowVersion = draft.RowVersion,
            Lines = [new() { ItemId = secondItem, Quantity = 4, UnitPrice = 5 }, new() { ItemId = ids.Item, Quantity = 6, UnitPrice = 7 }]
        };
        var updated = await service.UpdateAsync(draft.Id, update, default);
        Assert.NotEqual(draft.RowVersion, updated.RowVersion);
        await Conflict(() => service.UpdateAsync(draft.Id, update, default));
        await Conflict(() => service.CancelAsync(draft.Id, draft.RowVersion, default));
        Assert.Equal(62m, updated.TotalAmount);
        Assert.Equal(stock, await Stock(before));
        var cancelled = await service.CancelAsync(draft.Id, updated.RowVersion, default);
        Assert.Equal(DocumentStatus.Cancelled, cancelled.Status); Assert.Equal(f.ActorId, cancelled.CancelledByUserId); Assert.NotNull(cancelled.CancelledAt);
        var persisted = await service.GetAsync(draft.Id, default);
        Assert.Equal(updated.Lines.ToArray(), persisted.Lines.ToArray());
        Assert.Equal(2, await before.SalesOrderLines.CountAsync());
        Assert.Equal(stock, await Stock(before)); Assert.Equal(DocumentStatus.Cancelled, (await before.SalesOrders.AsNoTracking().SingleAsync()).Status);
        await Conflict(() => service.CancelAsync(draft.Id, cancelled.RowVersion, default));
        await Conflict(() => service.UpdateAsync(draft.Id, new() { CustomerId = ids.Customer, RowVersion = cancelled.RowVersion, Lines = update.Lines }, default));
        var page = await service.ListAsync(new() { CustomerId = ids.Customer, Status = DocumentStatus.Cancelled, From = DateTimeOffset.UnixEpoch, To = DateTimeOffset.UnixEpoch.AddDays(1), PageSize = 1, SortBy = "id", SortDirection = "asc" }, default);
        Assert.Equal(draft.Id, Assert.Single(page.Items).Id);
        Assert.Empty((await service.ListAsync(new() { CustomerId = int.MaxValue }, default)).Items);
        Assert.Empty((await service.ListAsync(new() { Status = DocumentStatus.Draft }, default)).Items);
        Assert.Empty((await service.ListAsync(new() { To = DateTimeOffset.UnixEpoch }, default)).Items);
        Assert.Empty((await service.ListAsync(new() { Page = 2, PageSize = 1 }, default)).Items);
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => service.GetAsync(int.MaxValue, default))).StatusCode);
    }
    [SalesSqlFact]
    public async Task InactiveCustomerRawOrInactiveItemAreRejectedAndUsedItemCannotChangeClassification()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Sales"); var ids = await Setup(f);
        await using (var db = f.Context()) { var customer = await db.Customers.SingleAsync(); customer.IsActive = false; await db.SaveChangesAsync(); }
        await using (var db = f.Context()) await Conflict(() => f.Sales(db).CreateAsync(Create(ids.Customer, ids.Item), default));
        await using (var db = f.Context()) { (await db.Customers.SingleAsync()).IsActive = true; var item = await db.Items.SingleAsync(); item.ItemType = ItemType.RawMaterial; await db.SaveChangesAsync(); }
        await using (var db = f.Context()) await Conflict(() => f.Sales(db).CreateAsync(Create(ids.Customer, ids.Item), default));
        await using (var db = f.Context()) { var item = await db.Items.SingleAsync(); item.ItemType = ItemType.FinishedProduct; item.IsActive = false; await db.SaveChangesAsync(); }
        await using (var db = f.Context()) await Conflict(() => f.Sales(db).CreateAsync(Create(ids.Customer, ids.Item), default));
        await using (var db = f.Context()) { (await db.Items.SingleAsync()).IsActive = true; await db.SaveChangesAsync(); await f.Sales(db).CreateAsync(Create(ids.Customer, ids.Item), default); }
        await using (var change = f.Context())
        {
            var order = await change.SalesOrders.AsNoTracking().SingleAsync();
            var item = await change.Items.SingleAsync();
            item.IsActive = false;
            await change.SaveChangesAsync();
            await using var attempt = f.Context();
            var request = new UpdateSalesOrderRequest
            {
                CustomerId = ids.Customer,
                RowVersion = Convert.ToBase64String(order.RowVersion),
                Lines = [new() { ItemId = ids.Item, Quantity = 2, UnitPrice = 3 }]
            };
            await Conflict(() => f.Sales(attempt).UpdateAsync(order.Id, request, default));
            await using var verify = f.Context();
            var saved = await f.Sales(verify).GetAsync(order.Id, default);
            Assert.Equal(request.RowVersion, saved.RowVersion);
            Assert.Equal(.03m, saved.TotalAmount);
            item.IsActive = true;
            await change.SaveChangesAsync();
        }
        await using var check = f.Context(); var dto = await f.Items(check).GetAsync(ids.Item, default);
        await Conflict(() => f.Items(check).UpdateAsync(dto.Id, new() { Code = dto.Code, Name = dto.Name, CategoryId = dto.CategoryId, Unit = "box", ItemType = ItemType.FinishedProduct, RowVersion = dto.RowVersion }, default));
    }
    [SalesSqlFact]
    public async Task RollbackAfterHeaderAndLineRemovalRestoresWholeOrderAndStock()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Sales"); var ids = await Setup(f);
        SalesOrderResponse original;
        await using (var db = f.Context()) original = await f.Sales(db).CreateAsync(Create(ids.Customer, ids.Item), default);
        await using var before = f.Context(); await before.Database.OpenConnectionAsync(); var stock = await Stock(before);
        var failure = new FailNewLines();
        await using (var db = f.Context(failure)) await Assert.ThrowsAsync<DbUpdateException>(() => f.Sales(db).UpdateAsync(original.Id, new()
        {
            CustomerId = ids.Customer,
            RowVersion = original.RowVersion,
            Lines = [new() { ItemId = ids.Item, Quantity = 9, UnitPrice = 2 }]
        }, default));
        Assert.Equal(2, failure.Saves);
        await using var verify = f.Context(); await verify.Database.OpenConnectionAsync(); var current = await f.Sales(verify).GetAsync(original.Id, default);
        Assert.Equal(original.RowVersion, current.RowVersion); Assert.Equal(original.TotalAmount, current.TotalAmount); Assert.Equal(original.Lines.ToArray(), current.Lines.ToArray());
        Assert.Equal(stock, await Stock(verify));
        await using (var db = f.Context(new InvalidLineOnCreate()))
        {
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => f.Sales(db).CreateAsync(Create(ids.Customer, ids.Item), default));
            Assert.Contains("CK_SalesOrderLines_Quantity", Assert.IsType<SqlException>(error.InnerException).Message);
        }
        await using (var db = f.Context(new FailAfterSave())) await Assert.ThrowsAsync<InvalidOperationException>(() => f.Sales(db).CreateAsync(Create(ids.Customer, ids.Item), default));
        Assert.Equal(1, await verify.SalesOrders.CountAsync()); Assert.Equal(1, await verify.SalesOrderLines.CountAsync()); Assert.Equal(stock, await Stock(verify));
    }
    [SalesSqlFact]
    public async Task ConcurrentCreatesHaveDifferentNumbersAndForcedNumberCollisionIs409()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Sales"); var ids = await Setup(f); var barrier = new TwoHeaderLocks("FROM [Items] WITH (UPDLOCK, HOLDLOCK)");
        async Task<SalesOrderResponse> CreateOne() { await using var db = f.Context(barrier); return await f.Sales(db).CreateAsync(Create(ids.Customer, ids.Item), default); }
        var pair = await Task.WhenAll(CreateOne(), CreateOne()); Assert.Equal(2, barrier.ConnectionIds.Count); Assert.NotEqual(pair[0].OrderNumber, pair[1].OrderNumber);
        await using (var db = f.Context(new ForceNumber(pair[0].OrderNumber))) await Conflict(() => f.Sales(db).CreateAsync(Create(ids.Customer, ids.Item), default));
        await using var verify = f.Context(); Assert.Equal(2, await verify.SalesOrders.CountAsync());
    }
    [SalesSqlFact]
    public async Task ConcurrentUpdateAndCancelWithSameVersionHaveOneWinner()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Sales"); var ids = await Setup(f);
        SalesOrderResponse order; await using (var db = f.Context()) order = await f.Sales(db).CreateAsync(Create(ids.Customer, ids.Item), default);
        var barrier = new TwoHeaderLocks();
        async Task<int> Execute(bool cancel)
        {
            await using var db = f.Context(barrier);
            try
            {
                if (cancel) await f.Sales(db).CancelAsync(order.Id, order.RowVersion, default);
                else await f.Sales(db).UpdateAsync(order.Id, new()
                {
                    CustomerId = ids.Customer,
                    RowVersion = order.RowVersion,
                    Note = "winner",
                    Lines = [new() { ItemId = ids.Item, Quantity = 2, UnitPrice = 5 }]
                }, default);
                return 200;
            }
            catch (ApiException e) { return e.StatusCode; }
        }
        var results = await Task.WhenAll(Execute(false), Execute(true));
        Assert.Equal(new[] { 200, 409 }, results.Order());
        Assert.Equal(2, barrier.ConnectionIds.Count);
        await using var verify = f.Context();
        var saved = await f.Sales(verify).GetAsync(order.Id, default);
        Assert.Equal(results[0] == 200 ? DocumentStatus.Draft : DocumentStatus.Cancelled, saved.Status);
        Assert.Equal(results[0] == 200 ? 10m : order.TotalAmount, saved.TotalAmount);
        Assert.Equal(saved.TotalAmount, Assert.Single(saved.Lines).LineTotal);
        Assert.NotEqual(order.RowVersion, saved.RowVersion);
    }
    [SalesSqlFact]
    public async Task SqlConstraintsRejectDuplicateLinesInvalidForeignKeysQuantityAndPrice()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Sales"); var ids = await Setup(f);
        SalesOrderResponse order; await using (var db = f.Context()) order = await f.Sales(db).CreateAsync(Create(ids.Customer, ids.Item), default);
        int secondItem;
        await using (var setup = f.Context()) secondItem = (await f.Items(setup).CreateAsync(new() { Code = "SECOND", Name = "Second", CategoryId = await setup.Categories.Select(x => x.Id).SingleAsync(), Unit = "kg", ItemType = ItemType.FinishedProduct }, default)).Id;
        foreach (var kind in new[] { "itemDuplicate", "lineDuplicate", "itemFk", "orderFk", "quantity", "price" })
        {
            await using var db = f.Context(); await using var tx = await db.Database.BeginTransactionAsync();
            var line = new SalesOrderLine { SalesOrderId = order.Id, ItemId = ids.Item, LineNumber = 2, Quantity = 1, UnitPrice = 1, LineTotal = 1 };
            if (kind == "lineDuplicate") { line.LineNumber = 1; line.ItemId = secondItem; }
            if (kind == "itemFk") line.ItemId = int.MaxValue;
            if (kind == "orderFk") line.SalesOrderId = int.MaxValue;
            if (kind == "quantity") line.Quantity = 0;
            if (kind == "price") line.UnitPrice = -1;
            // Xóa dòng sở hữu trong transaction để CHECK/FK không bị unique che lỗi; rollback cuối case.
            if (kind is not ("itemDuplicate" or "lineDuplicate")) await db.SalesOrderLines.Where(x => x.SalesOrderId == order.Id).ExecuteDeleteAsync();
            db.SalesOrderLines.Add(line); var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var sql = Assert.IsType<SqlException>(error.InnerException);
            if (kind.EndsWith("Duplicate")) Assert.Contains(sql.Number, new[] { 2601, 2627 }); else Assert.Equal(547, sql.Number);
            Assert.Contains(kind switch
            {
                "itemDuplicate" => "IX_SalesOrderLines_SalesOrderId_ItemId",
                "lineDuplicate" => "IX_SalesOrderLines_SalesOrderId_LineNumber",
                "itemFk" => "FK_SalesOrderLines_Items_ItemId",
                "orderFk" => "FK_SalesOrderLines_SalesOrders_SalesOrderId",
                "quantity" => "CK_SalesOrderLines_Quantity",
                _ => "CK_SalesOrderLines_UnitPrice"
            }, sql.Message);
            await tx.RollbackAsync();
        }
        await using (var db = f.Context()) { var row = await db.SalesOrders.SingleAsync(); row.CustomerId = int.MaxValue; var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number); }
    }
    private sealed class FailNewLines : SaveChangesInterceptor
    {
        public int Saves;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        { if (++Saves == 2) foreach (var entry in data.Context!.ChangeTracker.Entries<SalesOrderLine>().Where(x => x.State == EntityState.Added)) entry.Entity.Quantity = 0; return ValueTask.FromResult(result); }
    }
    private sealed class InvalidLineOnCreate : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        { foreach (var entry in data.Context!.ChangeTracker.Entries<SalesOrderLine>().Where(x => x.State == EntityState.Added)) entry.Entity.Quantity = 0; return ValueTask.FromResult(result); }
    }
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default) => throw new InvalidOperationException("Test-only failure before commit.");
    }
    private sealed class ForceNumber(string number) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        { foreach (var e in data.Context!.ChangeTracker.Entries<SalesOrder>().Where(x => x.State == EntityState.Added)) e.Entity.OrderNumber = number; return ValueTask.FromResult(result); }
    }
    private sealed class TwoHeaderLocks(string fragment = "FROM [SalesOrders] WITH (UPDLOCK, HOLDLOCK)") : DbCommandInterceptor
    {
        public System.Collections.Concurrent.ConcurrentDictionary<Guid, bool> ConnectionIds { get; } = new();
        private int count; private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { if (command.CommandText.Contains(fragment)) { ConnectionIds.TryAdd(((SqlConnection)command.Connection!).ClientConnectionId, true); if (Interlocked.Increment(ref count) == 2) ready.TrySetResult(); await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), ct); } return result; }
    }
}
