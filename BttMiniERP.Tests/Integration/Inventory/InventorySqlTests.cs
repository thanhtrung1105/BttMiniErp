using System.Data.Common;
using BttMiniERP.Api.Common;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.DTOs.Items;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
using BttMiniERP.Api.Services;
using BttMiniERP.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BttMiniERP.Tests.Integration.Inventory;

[Collection("MasterData SQL"), Trait("Category", "SqlServer"), Trait("SqlSuite", "Inventory")]
public sealed class InventorySqlTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static InventoryMovement Receipt(int id, decimal quantity, int source = 1, int line = 1)
        => new(id, quantity, InventoryTransactionType.PurchaseReceipt, SourceDocumentType.PurchaseReceipt, source, line);
    private static InventoryMovement Consumption(int id, decimal quantity, int source = 1, int line = 1)
        => new(id, -quantity, InventoryTransactionType.ProductionConsumption, SourceDocumentType.ProductionOrder, source, line);
    private static async Task<ItemResponse> Item(MasterDataSqlFixture f, ApplicationDbContext db, string code = "RAW", ItemType type = ItemType.RawMaterial)
    {
        var category = await f.Categories(db).CreateAsync(new() { Code = code, Name = code }, Ct);
        return await f.Items(db).CreateAsync(new() { Code = code, Name = code, CategoryId = category.Id, Unit = "kg", ItemType = type }, Ct);
    }
    private static async Task Apply(MasterDataSqlFixture f, params InventoryMovement[] movements)
    {
        await using var db = f.Context();
        await using var tx = await db.Database.BeginTransactionAsync();
        await f.Inventory(db).ApplyAsync(movements, Ct);
        Assert.Same(tx, db.Database.CurrentTransaction);
        await tx.CommitAsync();
    }
    private static async Task Conflict(Func<Task> action) => Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(action)).StatusCode);

    [InventorySqlFact]
    public async Task ItemCreatesZeroBalanceAndFailureToSaveBalanceRollsBackItem()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Inventory");
        await using var db = f.Context();
        var item = await Item(f, db);
        var balance = await db.InventoryBalances.AsNoTracking().SingleAsync();
        Assert.Equal(item.Id, balance.ItemId); Assert.Equal(0m, balance.QuantityOnHand);
        Assert.Equal(item.CreatedAt, balance.CreatedAt); Assert.Equal(f.ActorId, balance.CreatedByUserId);
        Assert.Null(balance.UpdatedAt); Assert.Equal(8, balance.RowVersion.Length);
        Assert.Empty(await db.InventoryTransactions.ToListAsync());
        var failure = new RejectBalanceSave();
        await using (var broken = f.Context(failure))
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Items(broken).CreateAsync(new() {
                Code = "FAIL", Name = "Fail", CategoryId = item.CategoryId, Unit = "kg", ItemType = ItemType.RawMaterial }, Ct));
        Assert.True(failure.ReachedBalanceSave);
        Assert.Equal(item.Id, (await db.Items.AsNoTracking().SingleAsync()).Id);
        Assert.Equal(item.Id, (await db.InventoryBalances.AsNoTracking().SingleAsync()).ItemId);
    }

    [InventorySqlFact]
    public async Task ReceiptConsumptionOutputAndSalePreserveFractionalQuantityAuditAndBalanceAfter()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Inventory");
        await using var db = f.Context();
        var raw = await Item(f, db); var product = await Item(f, db, "PRODUCT", ItemType.FinishedProduct);
        var initialVersion = (await db.InventoryBalances.AsNoTracking().SingleAsync(x => x.ItemId == raw.Id)).RowVersion;
        await Apply(f, Receipt(raw.Id, 3.123m));
        await Apply(f, new(product.Id, 1.001m, InventoryTransactionType.ProductionOutput, SourceDocumentType.ProductionOrder, 1, 0), Consumption(raw.Id, 2.002m));
        await Apply(f, new InventoryMovement(product.Id, -0.001m, InventoryTransactionType.SalesIssue, SourceDocumentType.SalesOrder, 1, 1));
        var balances = await db.InventoryBalances.AsNoTracking().OrderBy(x => x.ItemId).ToArrayAsync();
        Assert.Equal(new[] { 1.121m, 1m }, balances.Select(x => x.QuantityOnHand));
        Assert.NotEqual(initialVersion, balances[0].RowVersion);
        Assert.All(balances, x => { Assert.Equal(f.ActorId, x.UpdatedByUserId); Assert.NotNull(x.UpdatedAt); });
        var ledger = await db.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        Assert.Equal(new[] { 3.123m, -2.002m, 1.001m, -0.001m }, ledger.Select(x => x.QuantityDelta));
        Assert.Equal(new[] { 3.123m, 1.121m, 1.001m, 1m }, ledger.Select(x => x.BalanceAfter));
        Assert.All(ledger, x => { Assert.Equal(f.ActorId, x.PerformedByUserId); Assert.Equal(TimeSpan.Zero, x.OccurredAt.Offset); Assert.True(x.OccurredAt > raw.CreatedAt); });
    }

    [InventorySqlFact]
    public async Task OverdrawAndDuplicateSourceRestoreBalanceEvenWhenCallerCommitsAfterFailure()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Inventory");
        await using var db = f.Context(); var item = await Item(f, db);
        await Apply(f, Receipt(item.Id, 3.001m));
        foreach (var failed in new[] { Consumption(item.Id, 4m), Receipt(item.Id, 3.001m) })
        {
            await using var check = f.Context(); await using var tx = await check.Database.BeginTransactionAsync();
            await Conflict(() => f.Inventory(check).ApplyAsync([failed], Ct));
            await tx.CommitAsync();
        }
        Assert.Equal(3.001m, (await db.InventoryBalances.AsNoTracking().SingleAsync()).QuantityOnHand);
        Assert.Single(await db.InventoryTransactions.ToArrayAsync());
    }

    [InventorySqlFact]
    public async Task LaterLineFailureAndLedgerInsertFailureRollbackWholeBatch()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Inventory");
        await using var db = f.Context(); var first = await Item(f, db); var second = await Item(f, db, "SECOND");
        await using (var check = f.Context())
        {
            await using var tx = await check.Database.BeginTransactionAsync();
            await Conflict(() => f.Inventory(check).ApplyAsync([Receipt(first.Id, 1), Consumption(second.Id, 1)], Ct));
            await tx.CommitAsync();
        }
        var failure = new LedgerFailure();
        await using (var check = f.Context(failure))
        {
            await using var tx = await check.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Inventory(check).ApplyAsync([Receipt(first.Id, 1)], Ct));
            Assert.True(failure.ReachedInsert);
            await tx.CommitAsync();
        }
        await using var verify = f.Context();
        Assert.All(await verify.InventoryBalances.AsNoTracking().ToArrayAsync(), x => Assert.Equal(0m, x.QuantityOnHand));
        Assert.Empty(await verify.InventoryTransactions.ToArrayAsync());
    }

    [InventorySqlFact]
    public async Task OuterRollbackRestoresBalanceLedgerAndUnrelatedCallerChange()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Inventory");
        await using var db = f.Context(); var item = await Item(f, db);
        await using (var check = f.Context())
        {
            await using var tx = await check.Database.BeginTransactionAsync();
            var entity = await check.Items.SingleAsync(); entity.Name = "Caller change"; await check.SaveChangesAsync();
            await f.Inventory(check).ApplyAsync([Receipt(item.Id, 2)], Ct);
            Assert.Equal(2m, (await check.InventoryBalances.AsNoTracking().SingleAsync()).QuantityOnHand);
            Assert.Single(await check.InventoryTransactions.ToArrayAsync());
            await tx.RollbackAsync();
        }
        await using var verify = f.Context();
        Assert.Equal(item.Name, (await verify.Items.AsNoTracking().SingleAsync()).Name);
        Assert.Equal(0m, (await verify.InventoryBalances.AsNoTracking().SingleAsync()).QuantityOnHand);
        Assert.Empty(await verify.InventoryTransactions.ToArrayAsync());
    }

    [InventorySqlFact]
    public async Task DatabaseRejectsNegativeBalanceZeroDeltaAndInvalidItemOrActor()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Inventory");
        await using var db = f.Context(); var item = await Item(f, db);
        var negative = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE InventoryBalances SET QuantityOnHand=-1 WHERE ItemId={item.Id}"));
        Assert.Equal(547, negative.Number); Assert.Contains("CK_InventoryBalances_Quantity", negative.Message);
        foreach (var scenario in new[] { (Item: item.Id, Actor: f.ActorId, Delta: 0, Constraint: "CK_InventoryTransactions_Delta"),
            (Item: int.MaxValue, Actor: f.ActorId, Delta: 1, Constraint: "FK_InventoryTransactions_Items_ItemId"),
            (Item: item.Id, Actor: "missing-actor", Delta: 1, Constraint: "FK_InventoryTransactions_AspNetUsers_PerformedByUserId") })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO InventoryTransactions (ItemId,TransactionType,QuantityDelta,BalanceAfter,SourceDocumentType,SourceDocumentId,SourceLineId,PerformedByUserId,OccurredAt)
                VALUES ({scenario.Item},0,{scenario.Delta},1,0,91,1,{scenario.Actor},{DateTimeOffset.UtcNow})
                """));
            Assert.Equal(547, error.Number);
            // Delta=0 đồng thời vi phạm CHECK dấu; SQL Server không bảo đảm thứ tự đánh giá CHECK.
            if (scenario.Delta != 0) Assert.Contains(scenario.Constraint, error.Message);
        }
        Assert.Equal(0m, (await db.InventoryBalances.AsNoTracking().SingleAsync()).QuantityOnHand);
        Assert.Empty(await db.InventoryTransactions.ToArrayAsync());
    }

    [InventorySqlFact]
    public async Task TwoIndependentTransactionsCannotBothConsumeTheSameStock()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Inventory");
        await using var db = f.Context(); var item = await Item(f, db); await Apply(f, Receipt(item.Id, 3));
        var barrier = new TwoItemLockAttempts();
        await using var first = f.Context(barrier); await using var second = f.Context(barrier);
        async Task<int> Consume(ApplicationDbContext context, int source)
        {
            await using var tx = await context.Database.BeginTransactionAsync();
            try { await f.Inventory(context).ApplyAsync([Consumption(item.Id, 2, source)], Ct); await tx.CommitAsync(); return 200; }
            catch (ApiException e) { await tx.RollbackAsync(); return e.StatusCode; }
        }
        Assert.Equal(new[] { 200, 409 }, (await Task.WhenAll(Consume(first, 1), Consume(second, 2))).Order());
        Assert.NotSame(first.Database.GetDbConnection(), second.Database.GetDbConnection());
        Assert.Equal(1m, (await db.InventoryBalances.AsNoTracking().SingleAsync()).QuantityOnHand);
        Assert.Equal(2, await db.InventoryTransactions.CountAsync());
        Assert.Single(await db.InventoryTransactions.Where(x => x.QuantityDelta < 0).ToArrayAsync());
        Assert.Null(first.Database.CurrentTransaction); Assert.Null(second.Database.CurrentTransaction);
    }

    [InventorySqlFact]
    public async Task ReversedMultiItemBatchesUseSameSqlOrderAndDoNotLoseUpdates()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Inventory");
        await using var db = f.Context(); var a = await Item(f, db); var b = await Item(f, db, "SECOND");
        var barrier = new TwoItemLockAttempts(); var firstOrder = new UpdateOrder(); var secondOrder = new UpdateOrder();
        async Task Batch(int source, bool reverse, UpdateOrder order)
        {
            await using var context = f.Context(barrier, order); await using var tx = await context.Database.BeginTransactionAsync();
            var movements = new[] { Receipt(a.Id, 1, source, 1), Receipt(b.Id, 1, source, 2) };
            await f.Inventory(context).ApplyAsync(reverse ? movements.Reverse().ToArray() : movements, Ct);
            await tx.CommitAsync();
        }
        await Task.WhenAll(Batch(1, true, firstOrder), Batch(2, false, secondOrder));
        Assert.Equal(new[] { a.Id, b.Id }, firstOrder.Items); Assert.Equal(firstOrder.Items, secondOrder.Items);
        Assert.All(await db.InventoryBalances.AsNoTracking().ToArrayAsync(), x => Assert.Equal(2m, x.QuantityOnHand));
        Assert.Equal(4, await db.InventoryTransactions.CountAsync());
    }

    [InventorySqlFact]
    public async Task MaximumQuantityAndStaleTrackerCannotOverwriteInventory()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Inventory");
        await using var db = f.Context(); var item = await Item(f, db);
        var tracked = await db.InventoryBalances.SingleAsync();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await f.Inventory(db).ApplyAsync([Receipt(item.Id, InventoryMovement.MaximumQuantity)], Ct);
            Assert.Equal(EntityState.Detached, db.Entry(tracked).State);
            await db.SaveChangesAsync(); await tx.CommitAsync();
        }
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await Conflict(() => f.Inventory(db).ApplyAsync([Receipt(item.Id, 0.001m, 2)], Ct));
            await tx.CommitAsync();
        }
        Assert.Equal(InventoryMovement.MaximumQuantity, (await db.InventoryBalances.AsNoTracking().SingleAsync()).QuantityOnHand);
        Assert.Single(await db.InventoryTransactions.ToArrayAsync());
        await using var fresh = f.Context();
        await Conflict(() => f.Items(fresh).UpdateAsync(item.Id, new() { Code = item.Code, Name = item.Name, CategoryId = item.CategoryId,
            ItemType = ItemType.FinishedProduct, Unit = item.Unit, RowVersion = item.RowVersion }, Ct));
    }

    [InventorySqlFact]
    public async Task ConcurrentDuplicateSourceChangesBalanceOnlyOnce()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Inventory");
        await using var setup = f.Context(); var item = await Item(f, setup);
        var barrier = new TwoItemLockAttempts();
        await using var first = f.Context(barrier); await using var second = f.Context(barrier);
        async Task<int> Receive(ApplicationDbContext db)
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            try { await f.Inventory(db).ApplyAsync([Receipt(item.Id, 1.123m)], Ct); await tx.CommitAsync(); return 200; }
            catch (ApiException error) { await tx.RollbackAsync(); return error.StatusCode; }
        }
        Assert.Equal(new[] { 200, 409 }, (await Task.WhenAll(Receive(first), Receive(second))).Order());
        await using var verify = f.Context();
        Assert.Equal(1.123m, (await verify.InventoryBalances.SingleAsync()).QuantityOnHand);
        var ledger = Assert.Single(await verify.InventoryTransactions.ToArrayAsync());
        Assert.Equal(1.123m, ledger.QuantityDelta); Assert.Equal(1.123m, ledger.BalanceAfter);
    }

    private sealed class RejectBalanceSave : SaveChangesInterceptor
    {
        public bool ReachedBalanceSave { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (data.Context!.ChangeTracker.Entries<InventoryBalance>().Any(x => x.State == EntityState.Added))
            { ReachedBalanceSave = true; throw new InvalidOperationException("Test-only balance failure."); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class LedgerFailure : DbCommandInterceptor
    {
        public bool ReachedInsert { get; private set; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO [InventoryTransactions]", StringComparison.Ordinal))
            { ReachedInsert = true; throw new InvalidOperationException("Test-only ledger failure."); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class UpdateOrder : DbCommandInterceptor
    {
        public List<int> Items { get; } = [];
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE [InventoryBalances]", StringComparison.Ordinal))
                Items.Add((int)Assert.Single(command.Parameters.Cast<DbParameter>(), x => x.Value is int).Value!);
            return ValueTask.FromResult(result);
        }
    }
}
