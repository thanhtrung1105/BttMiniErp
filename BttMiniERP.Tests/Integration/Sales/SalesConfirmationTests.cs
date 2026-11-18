using System.Data.Common;
using System.Text.Json;
using BttMiniERP.Api.Common;
using BttMiniERP.Api.DTOs.SalesOrders;
using BttMiniERP.Api.Enums;
using BttMiniERP.Api.Services;
using BttMiniERP.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BttMiniERP.Tests.Integration.Sales;

public sealed partial class SalesOrdersSqlTests
{
    private static async Task SupplyProduct(MasterDataSqlFixture f, int item, decimal quantity)
    {
        await using var db = f.Context();
        await using var tx = await db.Database.BeginTransactionAsync();
        await f.Inventory(db).ApplyAsync([new(item, quantity, InventoryTransactionType.ProductionOutput,
            SourceDocumentType.ProductionOrder, item, 0)], default);
        await tx.CommitAsync();
    }

    private static async Task<SalesOrderResponse> TwoLineDraft(MasterDataSqlFixture f, decimal lastStock)
    {
        var (customer, first) = await Setup(f);
        await using var db = f.Context();
        var second = await f.Items(db).CreateAsync(new() { Code = "FG2", Name = "Second",
            CategoryId = await db.Categories.Select(x => x.Id).SingleAsync(), Unit = "kg", ItemType = ItemType.FinishedProduct }, default);
        await SupplyProduct(f, first, 3);
        await SupplyProduct(f, second.Id, lastStock);
        // Dòng có ItemId lớn hơn đứng trước để phân biệt thứ tự chứng từ với thứ tự ghi kho.
        return await f.Sales(db).CreateAsync(new() { CustomerId = customer, Lines = [
            new() { ItemId = second.Id, Quantity = 2, UnitPrice = 1 },
            new() { ItemId = first, Quantity = 1.125m, UnitPrice = 2 }] }, default);
    }

    private static async Task<SalesOrderResponse> Confirm(MasterDataSqlFixture f, SalesOrderResponse order, params IInterceptor[] interceptors)
    {
        await using var db = f.Context(interceptors);
        return await f.Sales(db).ConfirmAsync(order.Id, order.RowVersion, default);
    }

    private static async Task<string> Snapshot(MasterDataSqlFixture f, int orderId)
    {
        await using var db = f.Context();
        await db.Database.OpenConnectionAsync();
        var order = await f.Sales(db).GetAsync(orderId, default);
        return JsonSerializer.Serialize(new { order, stock = await Stock(db) });
    }

    [SalesSqlFact]
    public async Task ConfirmationPostsPersistedLinesInItemOrderAndCannotBeRepeated()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Sales");
        var draft = await TwoLineDraft(f, 3);
        await Conflict(() => Confirm(f, draft with { RowVersion = Convert.ToBase64String(new byte[8]) }));
        var result = await Confirm(f, draft);
        await using var db = f.Context();
        var persisted = await f.Sales(db).GetAsync(draft.Id, default);
        Assert.Equal(DocumentStatus.Confirmed, persisted.Status);
        Assert.NotNull(persisted.ConfirmedAt);
        Assert.Equal(f.ActorId, persisted.ConfirmedByUserId);
        Assert.Equal(persisted.ConfirmedAt, persisted.UpdatedAt);
        Assert.Equal(f.ActorId, persisted.UpdatedByUserId);
        Assert.Equal(result.RowVersion, persisted.RowVersion);
        Assert.NotEqual(draft.RowVersion, persisted.RowVersion);
        Assert.Equal(draft.Lines.ToArray(), persisted.Lines.ToArray());
        Assert.Equal(draft.TotalAmount, persisted.TotalAmount);
        var entries = await db.InventoryTransactions.Where(x => x.SourceDocumentType == SourceDocumentType.SalesOrder)
            .OrderBy(x => x.Id).ToArrayAsync();
        Assert.Equal(draft.Lines.Select(x => x.ItemId).Order(), entries.Select(x => x.ItemId));
        var balances = await db.InventoryBalances.ToDictionaryAsync(x => x.ItemId, x => x.QuantityOnHand);
        foreach (var entry in entries)
        {
            var line = draft.Lines.Single(x => x.Id == entry.SourceLineId);
            Assert.Equal(draft.Id, entry.SourceDocumentId);
            Assert.Equal(line.ItemId, entry.ItemId);
            Assert.Equal(InventoryTransactionType.SalesIssue, entry.TransactionType);
            Assert.Equal(-line.Quantity, entry.QuantityDelta);
            Assert.Equal(3m - line.Quantity, entry.BalanceAfter);
            Assert.Equal(entry.BalanceAfter, balances[line.ItemId]);
            Assert.Equal(f.ActorId, entry.PerformedByUserId);
        }
        var before = await Snapshot(f, draft.Id);
        await Conflict(() => Confirm(f, result));
        await Conflict(() => f.Sales(db).CancelAsync(result.Id, result.RowVersion, default));
        await Conflict(() => f.Sales(db).UpdateAsync(result.Id, new() { CustomerId = draft.CustomerId,
            Lines = [new() { ItemId = draft.Lines[0].ItemId, Quantity = 1 }], RowVersion = result.RowVersion }, default));
        Assert.Equal(before, await Snapshot(f, draft.Id));
    }

    [SalesSqlFact]
    public async Task ShortageInactiveReferencesAndCancellationPreventAnyPosting()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Sales");
        var draft = await TwoLineDraft(f, 1);
        var before = await Snapshot(f, draft.Id);
        await Conflict(() => Confirm(f, draft));
        Assert.Equal(before, await Snapshot(f, draft.Id));

        await using var db = f.Context();
        var customer = await db.Customers.SingleAsync();
        var item = await db.Items.FindAsync(draft.Lines[0].ItemId);
        Assert.NotNull(item);
        // Đủ tồn để lỗi tham chiếu không bị che bởi lỗi thiếu hàng.
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await f.Inventory(db).ApplyAsync([new(item.Id, 2, InventoryTransactionType.ProductionOutput,
                SourceDocumentType.ProductionOrder, int.MaxValue, 0)], default);
            await tx.CommitAsync();
        }
        foreach (var invalid in new[] { "customer", "inactive item", "raw material" })
        {
            customer.IsActive = invalid != "customer";
            item.IsActive = invalid != "inactive item";
            item.ItemType = invalid == "raw material" ? ItemType.RawMaterial : ItemType.FinishedProduct;
            await db.SaveChangesAsync();
            before = await Snapshot(f, draft.Id);
            await Conflict(() => Confirm(f, draft));
            Assert.Equal(before, await Snapshot(f, draft.Id));
        }
        item.ItemType = ItemType.FinishedProduct;
        await db.SaveChangesAsync();
        var cancelled = await f.Sales(db).CancelAsync(draft.Id, draft.RowVersion, default);
        before = await Snapshot(f, draft.Id);
        await Conflict(() => Confirm(f, cancelled));
        Assert.Equal(before, await Snapshot(f, draft.Id));
    }

    private sealed class FailAfterPosting(string statement, int occurrence) : DbCommandInterceptor
    {
        private int calls;
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(statement, StringComparison.Ordinal) && ++calls == occurrence)
                throw new InvalidOperationException("Injected sales posting failure.");
            return ValueTask.FromResult(result);
        }
    }

    [SalesSqlFact]
    public async Task PostingFailuresRestoreHeaderLinesAuditBalancesAndLedger()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Sales");
        var draft = await TwoLineDraft(f, 3);
        var before = await Snapshot(f, draft.Id);
        IInterceptor[] failures = [new FailAfterPosting("UPDATE [InventoryBalances]", 1),
            new FailAfterPosting("UPDATE [InventoryBalances]", 2),
            new FailAfterPosting("INSERT INTO [InventoryTransactions]", 2), new FailAfterSave()];
        foreach (var failure in failures)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Confirm(f, draft, failure));
            Assert.Equal(failure is FailAfterSave ? "Test-only failure before commit." : "Injected sales posting failure.", error.Message);
            Assert.Equal(before, await Snapshot(f, draft.Id));
        }
    }

    [SalesSqlFact]
    public async Task RacingConfirmationsAndDraftWritesCannotOversell()
    {
        foreach (var operation in new[] { "confirm", "second order", "update", "cancel" })
        {
            await using var f = await MasterDataSqlFixture.OpenAsync("Sales");
            var (customer, item) = await Setup(f);
            await SupplyProduct(f, item, 3);
            await using var setup = f.Context();
            var body = new CreateSalesOrderRequest { CustomerId = customer, Lines = [new() { ItemId = item, Quantity = 2, UnitPrice = 5 }] };
            var draft = await f.Sales(setup).CreateAsync(body, default);
            var other = operation == "second order" ? await f.Sales(setup).CreateAsync(body, default) : draft;
            var barrier = new TwoHeaderLocks(operation == "second order" ? "FROM [Items] WITH (UPDLOCK, HOLDLOCK)" : "FROM [SalesOrders] WITH (UPDLOCK, HOLDLOCK)");
            async Task<int> Execute(SalesOrderResponse target, string action)
            {
                await using var db = f.Context(barrier);
                var service = f.Sales(db);
                try
                {
                    if (action == "update")
                        await service.UpdateAsync(target.Id, new() { CustomerId = customer, Lines = body.Lines, RowVersion = target.RowVersion, Note = "edited" }, default);
                    else if (action == "cancel") await service.CancelAsync(target.Id, target.RowVersion, default);
                    else await service.ConfirmAsync(target.Id, target.RowVersion, default);
                    return 200;
                }
                catch (ApiException error) { return error.StatusCode; }
            }
            var results = await Task.WhenAll(Execute(draft, "confirm"), Execute(other, operation));
            Assert.Equal(new[] { 200, 409 }, results.Order());
            Assert.Equal(2, barrier.ConnectionIds.Count);
            await using var verify = f.Context();
            var posted = results[0] == 200 || operation is "confirm" or "second order";
            var ledger = await verify.InventoryTransactions.Where(x => x.SourceDocumentType == SourceDocumentType.SalesOrder).ToArrayAsync();
            var balance = await verify.InventoryBalances.Select(x => x.QuantityOnHand).SingleAsync();
            Assert.Equal(posted ? 1m : 3m, balance);
            var winner = results[0] == 200 ? draft : other;
            var saved = await f.Sales(verify).GetAsync(winner.Id, default);
            if (posted)
            {
                var entry = Assert.Single(ledger);
                Assert.Equal(winner.Id, entry.SourceDocumentId);
                Assert.Equal(winner.Lines.Single().Id, entry.SourceLineId);
                Assert.Equal(-2m, entry.QuantityDelta);
                Assert.Equal(balance, entry.BalanceAfter);
                Assert.Equal(DocumentStatus.Confirmed, saved.Status);
                Assert.Equal(f.ActorId, saved.ConfirmedByUserId);
                Assert.NotNull(saved.ConfirmedAt);
            }
            else
            {
                Assert.Empty(ledger);
                Assert.Null(saved.ConfirmedAt);
                Assert.Null(saved.ConfirmedByUserId);
                Assert.Equal(operation == "cancel" ? DocumentStatus.Cancelled : DocumentStatus.Draft, saved.Status);
                if (operation == "update") Assert.Equal("edited", saved.Note);
            }
            if (operation == "second order")
            {
                var loser = results[0] == 200 ? other : draft;
                var persisted = await f.Sales(verify).GetAsync(loser.Id, default);
                Assert.Equal(loser.Lines.ToArray(), persisted.Lines.ToArray());
                // Hai danh sách đã được so theo thứ tự và giá trị record; so toàn bộ header theo kiểu gốc.
                Assert.Equal(loser, persisted with { Lines = loser.Lines });
            }
        }
    }
}
