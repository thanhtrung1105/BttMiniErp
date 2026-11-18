using System.Data.Common;
using BttMiniERP.Api.Common;
using BttMiniERP.Api.DTOs.BillOfMaterials;
using BttMiniERP.Api.DTOs.ProductionOrders;
using BttMiniERP.Api.Enums;
using BttMiniERP.Api.Services;
using BttMiniERP.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BttMiniERP.Tests.Integration.Production;

public sealed partial class ProductionOrdersSqlTests
{
    private static async Task Supply(MasterDataSqlFixture fixture, BillOfMaterialResponse bom, decimal last = 10m)
    {
        await using var db = fixture.Context();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var movements = bom.Details.Select((line, i) => new InventoryMovement(line.RawMaterialId,
            i == bom.Details.Count - 1 ? last : 10m, InventoryTransactionType.PurchaseReceipt,
            SourceDocumentType.PurchaseReceipt, 1, line.Id)).ToArray();
        await fixture.Inventory(db).ApplyAsync(movements, default);
        await transaction.CommitAsync();
    }

    private static async Task<ProductionOrderResponse> Confirm(MasterDataSqlFixture fixture, ProductionOrderResponse order,
        params IInterceptor[] interceptors)
    {
        await using var db = fixture.Context(interceptors);
        return await fixture.Production(db).ConfirmAsync(order.Id, order.RowVersion, default);
    }

    [ProductionSqlFact]
    public async Task ConfirmPostsSnapshotAndAuditExactlyOnceAfterBomChanged()
    {
        await using var fixture = await MasterDataSqlFixture.OpenAsync("Production");
        var bom = await Setup(fixture);
        await Supply(fixture, bom);
        var order = await Create(fixture, bom.Id);
        await using (var edit = fixture.Context())
        {
            var changed = await fixture.Boms(edit).UpdateAsync(bom.Id, EditBom(bom), default);
            await fixture.Boms(edit).DeactivateAsync(bom.Id, changed.RowVersion, default);
        }

        var beforeStale = await Snapshot(fixture);
        var staleBytes = Convert.FromBase64String(order.RowVersion);
        staleBytes[0] ^= 1;
        await Conflict(() => Confirm(fixture, order with { RowVersion = Convert.ToBase64String(staleBytes) }));
        Assert.Equal(beforeStale, await Snapshot(fixture));

        var confirmed = await Confirm(fixture, order);
        await Conflict(() => Confirm(fixture, confirmed));
        await using var verify = fixture.Context();
        var saved = await fixture.Production(verify).GetAsync(order.Id, default);
        Assert.Equal(DocumentStatus.Confirmed, saved.Status);
        Assert.Equal(fixture.ActorId, saved.ConfirmedByUserId);
        Assert.NotNull(saved.ConfirmedAt);
        Assert.Equal(saved.ConfirmedAt, saved.UpdatedAt);
        Assert.Equal(confirmed.RowVersion, saved.RowVersion);
        Assert.NotEqual(order.RowVersion, saved.RowVersion);
        var ledger = await verify.InventoryTransactions.Where(x => x.SourceDocumentType == SourceDocumentType.ProductionOrder).ToListAsync();
        Assert.Equal(order.Materials.Count + 1, ledger.Count);
        var balances = await verify.InventoryBalances.ToDictionaryAsync(x => x.ItemId, x => x.QuantityOnHand);
        foreach (var line in saved.Materials)
        {
            var expected = order.Materials.Single(x => x.Id == line.Id).RequiredQuantity;
            Assert.Equal(expected, line.ActualQuantity);
            var entry = Assert.Single(ledger, x => x.ItemId == line.RawMaterialId);
            Assert.Equal(line.Id, entry.SourceLineId);
            Assert.Equal(InventoryTransactionType.ProductionConsumption, entry.TransactionType);
            Assert.Equal(-expected, entry.QuantityDelta);
            Assert.Equal(10m - expected, balances[line.RawMaterialId]);
        }
        var output = Assert.Single(ledger, x => x.ItemId == order.FinishedProductId);
        Assert.Equal(0, output.SourceLineId);
        Assert.Equal(InventoryTransactionType.ProductionOutput, output.TransactionType);
        Assert.Equal(order.Quantity, output.QuantityDelta);
        Assert.Equal(order.Quantity, balances[output.ItemId]);
        Assert.All(ledger, x => {
            Assert.Equal(order.Id, x.SourceDocumentId);
            Assert.Equal(fixture.ActorId, x.PerformedByUserId);
            Assert.Equal(balances[x.ItemId], x.BalanceAfter);
        });
    }

    [ProductionSqlFact]
    public async Task ShortageAndInvalidSnapshotItemsLeaveDraftAndInventoryUntouched()
    {
        await using var fixture = await MasterDataSqlFixture.OpenAsync("Production");
        var bom = await Setup(fixture);
        var order = await Create(fixture, bom.Id);
        await Supply(fixture, bom, 0.001m);
        var before = await Snapshot(fixture);
        await Conflict(() => Confirm(fixture, order));
        Assert.Equal(before, await Snapshot(fixture));

        foreach (var id in new[] { order.FinishedProductId, order.Materials[0].RawMaterialId })
        {
            await using var edit = fixture.Context();
            var item = await edit.Items.SingleAsync(x => x.Id == id);
            item.IsActive = false;
            await edit.SaveChangesAsync();
            before = await Snapshot(fixture);
            var error = await Assert.ThrowsAsync<ApiException>(() => Confirm(fixture, order));
            Assert.Equal(409, error.StatusCode);
            Assert.Contains("Item", error.Message);
            Assert.Equal(before, await Snapshot(fixture));
            item.IsActive = true;
            var originalType = item.ItemType;
            item.ItemType = originalType == ItemType.RawMaterial ? ItemType.FinishedProduct : ItemType.RawMaterial;
            await edit.SaveChangesAsync();
            before = await Snapshot(fixture);
            error = await Assert.ThrowsAsync<ApiException>(() => Confirm(fixture, order));
            Assert.Equal(409, error.StatusCode);
            Assert.Contains("Item", error.Message);
            Assert.Equal(before, await Snapshot(fixture));
            item.ItemType = originalType;
            await edit.SaveChangesAsync();
        }

        await using var db = fixture.Context();
        var missing = await Assert.ThrowsAsync<ApiException>(() => fixture.Production(db).ConfirmAsync(int.MaxValue, order.RowVersion, default));
        Assert.Equal(404, missing.StatusCode);
        var cancelled = await fixture.Production(db).CancelAsync(order.Id, order.RowVersion, default);
        await Conflict(() => Confirm(fixture, cancelled));
    }

    // Gây lỗi sau khi SQL đã ghi trong transaction.
    private sealed class FailAfterPosting(string sql, int occurrence) : DbCommandInterceptor
    {
        private int count;
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data,
            int result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains(sql, StringComparison.Ordinal) && ++count == occurrence)
                throw new InvalidOperationException("P4.3 injected failure");
            return ValueTask.FromResult(result);
        }
    }

    [ProductionSqlFact]
    public async Task PostingAndHeaderFailuresRestoreEveryBalanceLedgerLineAndAuditField()
    {
        await using var fixture = await MasterDataSqlFixture.OpenAsync("Production");
        var bom = await Setup(fixture);
        await Supply(fixture, bom);
        var order = await Create(fixture, bom.Id);
        var before = await Snapshot(fixture);
        // Setup tạo thành phẩm trước NVL nên theo thứ tự ItemId, lần cập nhật đầu là thành phẩm.
        IInterceptor[] failures = [new FailAfterPosting("UPDATE [InventoryBalances]", 1),
            new FailAfterPosting("UPDATE [InventoryBalances]", 2), new FailAfterPosting("INSERT INTO [InventoryTransactions]", 2),
            new FailAfterSave()];
        foreach (var failure in failures)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Confirm(fixture, order, failure));
            Assert.Contains(failure is FailAfterSave ? "before commit" : "injected failure", error.Message);
            Assert.Equal(before, await Snapshot(fixture));
        }
    }

    [ProductionSqlFact]
    public async Task ConcurrentConfirmationsAndDraftWritesHaveOneWinnerAndConsistentStock()
    {
        foreach (var competitor in new[] { "confirm", "another order", "update", "cancel" })
        {
            await using var fixture = await MasterDataSqlFixture.OpenAsync("Production");
            var bom = await Setup(fixture);
            await Supply(fixture, bom);
            var order = await Create(fixture, bom.Id);
            var other = competitor == "another order" ? await Create(fixture, bom.Id) : order;
            // Hai lệnh khác nhau có thể đi qua khóa header độc lập. Hẹn nhau ngay trước khóa Item chung.
            var barrier = new TwoHeaders(competitor == "another order"
                ? "FROM [Items] WITH (UPDLOCK, HOLDLOCK)"
                : "FROM [ProductionOrders] WITH (UPDLOCK, HOLDLOCK)");
            async Task<int> Execute(ProductionOrderResponse target, string operation)
            {
                await using var db = fixture.Context(barrier);
                var service = fixture.Production(db);
                try
                {
                    if (operation == "update")
                        await service.UpdateAsync(target.Id, new() { Quantity = 4, RowVersion = target.RowVersion }, default);
                    else if (operation == "cancel") await service.CancelAsync(target.Id, target.RowVersion, default);
                    else await service.ConfirmAsync(target.Id, target.RowVersion, default);
                    return 200;
                }
                catch (ApiException error) { return error.StatusCode; }
            }
            var results = await Task.WhenAll(Execute(order, "confirm"), Execute(other, competitor));
            Assert.Equal(new[] { 200, 409 }, results.Order());
            Assert.Equal(2, barrier.ConnectionIds.Count);
            await using var verify = fixture.Context();
            var orders = await verify.ProductionOrders.Include(x => x.Materials).ToListAsync();
            var posted = orders.Where(x => x.Status == DocumentStatus.Confirmed).ToList();
            var expectedCount = results[0] == 200 || competitor is "confirm" or "another order" ? 1 : 0;
            Assert.Equal(expectedCount, posted.Count);
            var ledger = await verify.InventoryTransactions.Where(x => x.SourceDocumentType == SourceDocumentType.ProductionOrder).ToListAsync();
            Assert.Equal(3 * expectedCount, ledger.Count);
            Assert.All(ledger, x => Assert.Equal(Assert.Single(posted).Id, x.SourceDocumentId));
            var balances = await verify.InventoryBalances.ToDictionaryAsync(x => x.ItemId, x => x.QuantityOnHand);
            Assert.Equal(3m * expectedCount, balances[bom.FinishedProductId]);
            foreach (var line in order.Materials)
                Assert.Equal(10m - expectedCount * line.RequiredQuantity, balances[line.RawMaterialId]);
            foreach (var saved in orders)
                Assert.All(saved.Materials, line => Assert.Equal(saved.Status == DocumentStatus.Confirmed ? line.RequiredQuantity : (decimal?)null, line.ActualQuantity));
        }
    }
}
