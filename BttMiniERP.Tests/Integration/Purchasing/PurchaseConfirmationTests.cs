using System.Data.Common;
using BttMiniERP.Api.Common;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.DTOs.PurchaseReceipts;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
using BttMiniERP.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BttMiniERP.Tests.Integration.Purchasing;

// Chỉ chạy sau phê duyệt riêng ConfirmationTest; không tự migrate hoặc reset dữ liệu.
[Collection("MasterData SQL"), Trait("Category", "SqlServer"), Trait("SqlSuite", "PurchaseConfirmation")]
public sealed class PurchaseConfirmationTests
{
    private static async Task<PurchaseReceiptResponse> Setup(MasterDataSqlFixture f)
    {
        await using var db = f.Context();
        var supplier = await f.Suppliers(db).CreateAsync(new() { Code = "CONFIRM", Name = "Confirm supplier" }, default);
        var category = await f.Categories(db).CreateAsync(new() { Code = "CONFIRM", Name = "Confirm category" }, default);
        var first = await f.Items(db).CreateAsync(new() { Code = "A", Name = "A", Unit = "kg", CategoryId = category.Id, ItemType = ItemType.RawMaterial }, default);
        var second = await f.Items(db).CreateAsync(new() { Code = "B", Name = "B", Unit = "kg", CategoryId = category.Id, ItemType = ItemType.RawMaterial }, default);
        return await f.Receipts(db).CreateAsync(new() { SupplierId = supplier.Id, Lines = [
            new() { ItemId = second.Id, Quantity = 2.125m, UnitPrice = 3 },
            new() { ItemId = first.Id, Quantity = 10, UnitPrice = 5 }] }, default);
    }
    private static async Task<PurchaseReceiptResponse> Confirm(MasterDataSqlFixture f, PurchaseReceiptResponse receipt, params IInterceptor[] interceptors)
    {
        // Mỗi lời gọi có connection/context riêng.
        await using var db = f.Context(interceptors);
        return await f.Receipts(db).ConfirmAsync(receipt.Id, receipt.RowVersion, default);
    }

    private static async Task<string[]> Snapshot(MasterDataSqlFixture f)
    {
        await using var db = f.Context(); await db.Database.OpenConnectionAsync();
        var values = new List<string>();
        foreach (var (table, key) in new[] { ("PurchaseReceipts", "Id"), ("PurchaseReceiptLines", "Id"), ("InventoryBalances", "ItemId"), ("InventoryTransactions", "Id") })
            values.Add(string.Join("", await IdentityDatabaseVerifier.ReadRowsAsync(db, $"SELECT * FROM [{table}] ORDER BY [{key}] FOR JSON PATH, INCLUDE_NULL_VALUES")));
        return values.ToArray();
    }
    private static async Task Conflict(Func<Task> action) => Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(action)).StatusCode);
    private static async Task VerifyConfirmed(MasterDataSqlFixture f, PurchaseReceiptResponse original)
    {
        await using var db = f.Context();
        var row = await db.PurchaseReceipts.Include(x => x.Lines).SingleAsync(x => x.Id == original.Id);
        Assert.Equal(DocumentStatus.Confirmed, row.Status); Assert.NotNull(row.ConfirmedAt);
        Assert.Equal(f.ActorId, row.ConfirmedByUserId); Assert.Equal(row.ConfirmedAt, row.UpdatedAt); Assert.Equal(f.ActorId, row.UpdatedByUserId);
        Assert.NotEqual(original.RowVersion, Convert.ToBase64String(row.RowVersion));
        Assert.Equal(original.TotalAmount, row.TotalAmount); Assert.Equal(original.CreatedAt, row.CreatedAt); Assert.Equal(original.CreatedByUserId, row.CreatedByUserId);
        Assert.Equal(original.Lines.Select(x => (x.Id,x.ItemId,x.Quantity,x.UnitPrice,x.LineTotal)),
            row.Lines.OrderBy(x => x.LineNumber).Select(x => (x.Id,x.ItemId,x.Quantity,x.UnitPrice,x.LineTotal)));
        var ledger = await db.InventoryTransactions.Where(x => x.SourceDocumentId == row.Id).ToArrayAsync();
        Assert.Equal(original.Lines.Count, ledger.Length);
        foreach (var line in original.Lines)
        {
            var movement = Assert.Single(ledger, x => x.SourceLineId == line.Id);
            Assert.Equal(line.ItemId, movement.ItemId); Assert.Equal(line.Quantity, movement.QuantityDelta);
            Assert.Equal(SourceDocumentType.PurchaseReceipt, movement.SourceDocumentType); Assert.Equal(InventoryTransactionType.PurchaseReceipt, movement.TransactionType);
            Assert.Equal(f.ActorId, movement.PerformedByUserId); Assert.True(movement.OccurredAt <= row.ConfirmedAt);
        }
    }

    [ConfirmationSqlFact]
    public async Task ConfirmUpdatesAuditBalanceAndSourceOnce()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Confirmation"); var draft = await Setup(f);
        var confirmed = await Confirm(f, draft);
        await VerifyConfirmed(f, draft);
        await using var db = f.Context();
        foreach (var line in draft.Lines)
        {
            var balance = await db.InventoryBalances.SingleAsync(x => x.ItemId == line.ItemId);
            Assert.Equal(line.Quantity, balance.QuantityOnHand); Assert.Equal(f.ActorId, balance.UpdatedByUserId); Assert.NotNull(balance.UpdatedAt);
            Assert.Equal(line.Quantity, (await db.InventoryTransactions.SingleAsync(x => x.ItemId == line.ItemId)).BalanceAfter);
        }
        var snapshot = await Snapshot(f);
        await Conflict(() => Confirm(f, draft)); await Conflict(() => Confirm(f, confirmed));
        Assert.Equal(snapshot, await Snapshot(f));
    }

    [ConfirmationSqlTheory]
    [InlineData("empty")] [InlineData("cancelled")] [InlineData("supplier")] [InlineData("inactiveItem")] [InlineData("finishedItem")] [InlineData("stale")]
    public async Task InvalidDraftOrChangedEligibilityCannotWriteStock(string reason)
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Confirmation"); var draft = await Setup(f);
        await using (var db = f.Context())
        {
            if (reason == "empty") draft = await f.Receipts(db).UpdateAsync(draft.Id, new() { SupplierId = draft.SupplierId, RowVersion = draft.RowVersion }, default);
            if (reason == "cancelled") draft = await f.Receipts(db).CancelAsync(draft.Id, draft.RowVersion, default);
            if (reason == "stale") await f.Receipts(db).UpdateAsync(draft.Id, new() { SupplierId = draft.SupplierId, RowVersion = draft.RowVersion, Note = "Changed" }, default);
            if (reason == "supplier") { (await db.Suppliers.SingleAsync()).IsActive = false; await db.SaveChangesAsync(); }
            if (reason is "inactiveItem" or "finishedItem")
            {
                var item = await db.Items.OrderBy(x => x.Id).LastAsync();
                if (reason == "inactiveItem") item.IsActive = false; else item.ItemType = ItemType.FinishedProduct;
                await db.SaveChangesAsync();
            }
        }
        var snapshot = await Snapshot(f); await Conflict(() => Confirm(f, draft)); Assert.Equal(snapshot, await Snapshot(f));
    }

    [ConfirmationSqlTheory]
    [InlineData("confirm")] [InlineData("edit")] [InlineData("cancel")]
    public async Task SameHeaderRacesHaveExactlyOneWinner(string competitor)
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Confirmation"); var draft = await Setup(f);
        var barrier = new HeaderBarrier();
        async Task<int> Run(bool confirming)
        {
            await using var db = f.Context(barrier); var service = f.Receipts(db);
            try
            {
                if (confirming || competitor == "confirm") await service.ConfirmAsync(draft.Id, draft.RowVersion, default);
                else if (competitor == "cancel") await service.CancelAsync(draft.Id, draft.RowVersion, default);
                else await service.UpdateAsync(draft.Id, new() { SupplierId = draft.SupplierId, RowVersion = draft.RowVersion, Note = "Edit won",
                    Lines = draft.Lines.Select(x => new PurchaseReceiptLineRequest { ItemId = x.ItemId, Quantity = x.Quantity + 1, UnitPrice = x.UnitPrice }).ToList() }, default);
                return 200;
            }
            catch (ApiException e) { return e.StatusCode; }
        }
        var results = await Task.WhenAll(Run(true), Run(false)); Assert.Equal(new[] { 200,409 }, results.Order());
        await using var verify = f.Context(); var row = await verify.PurchaseReceipts.SingleAsync();
        if (row.Status == DocumentStatus.Confirmed)
        {
            await VerifyConfirmed(f, draft);
            foreach (var line in draft.Lines) Assert.Equal(line.Quantity, (await verify.InventoryBalances.SingleAsync(x => x.ItemId == line.ItemId)).QuantityOnHand);
        }
        else
        {
            Assert.Equal(409, results[0]); Assert.Equal(200, results[1]);
            Assert.Equal(competitor == "cancel" ? DocumentStatus.Cancelled : DocumentStatus.Draft, row.Status);
            Assert.Null(row.ConfirmedAt); Assert.Null(row.ConfirmedByUserId);
            Assert.All(await verify.InventoryBalances.ToArrayAsync(), x => Assert.Equal(0m, x.QuantityOnHand));
            Assert.Empty(await verify.InventoryTransactions.ToArrayAsync());
            var lines = await verify.PurchaseReceiptLines.OrderBy(x => x.LineNumber).ToArrayAsync();
            Assert.Equal(draft.Lines.Count, lines.Length);
            Assert.Equal(draft.Lines.Select(x => (x.ItemId, x.Quantity + (competitor == "edit" ? 1 : 0), x.UnitPrice)),
                lines.Select(x => (x.ItemId, x.Quantity, x.UnitPrice)));
            if (competitor == "edit") Assert.Equal("Edit won", row.Note);
        }
    }

    [ConfirmationSqlFact]
    public async Task DifferentReceiptsForSameItemsDoNotLoseUpdates()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Confirmation"); var first = await Setup(f);
        PurchaseReceiptResponse second;
        await using (var db = f.Context()) second = await f.Receipts(db).CreateAsync(new() { SupplierId = first.SupplierId,
            Lines = first.Lines.Select(x => new PurchaseReceiptLineRequest { ItemId = x.ItemId, Quantity = x.Quantity, UnitPrice = x.UnitPrice }).ToList() }, default);
        var barrier = new HeaderBarrier(); await Task.WhenAll(Confirm(f, first, barrier), Confirm(f, second, barrier));
        await VerifyConfirmed(f, first); await VerifyConfirmed(f, second);
        await using var verify = f.Context();
        foreach (var line in first.Lines)
        {
            Assert.Equal(line.Quantity * 2, (await verify.InventoryBalances.SingleAsync(x => x.ItemId == line.ItemId)).QuantityOnHand);
            Assert.Equal(new[] { line.Quantity, line.Quantity * 2 }, await verify.InventoryTransactions.Where(x => x.ItemId == line.ItemId).OrderBy(x => x.BalanceAfter).Select(x => x.BalanceAfter).ToArrayAsync());
        }
        Assert.Equal(4, await verify.InventoryTransactions.CountAsync());
    }

    [ConfirmationSqlTheory]
    [InlineData("secondBalance")] [InlineData("ledger")] [InlineData("header")]
    public async Task FailuresAfterSqlWritesRollbackWholeDocument(string stage)
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Confirmation"); var draft = await Setup(f);
        var before = await Snapshot(f); var commandFailure = new StockFailure(stage); var headerFailure = new HeaderFailure();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Confirm(f, draft, stage == "header" ? headerFailure : commandFailure));
        if (stage == "secondBalance") { Assert.Equal(1, commandFailure.BalancesWritten); Assert.Equal(1, commandFailure.LedgersWritten); }
        if (stage == "ledger") { Assert.Equal(1, commandFailure.BalancesWritten); Assert.Equal(0, commandFailure.LedgersWritten); }
        if (stage == "header") Assert.True(headerFailure.FailedAfterSave);
        // Connection/context mới, toàn bộ cột header/lines/balance/ledger gồm audit/rowversion.
        Assert.Equal(before, await Snapshot(f));
    }

    [ConfirmationSqlFact]
    public async Task ExistingSourceConflictRollsBackAndLeavesDraft()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Confirmation"); var draft = await Setup(f);
        await using (var db = f.Context())
        {
            await using var tx = await db.Database.BeginTransactionAsync(); var line = draft.Lines.OrderBy(x => x.ItemId).First();
            await f.Inventory(db).ApplyAsync([new(line.ItemId, line.Quantity, InventoryTransactionType.PurchaseReceipt, SourceDocumentType.PurchaseReceipt, draft.Id, line.Id)], default);
            await tx.CommitAsync();
        }
        var snapshot = await Snapshot(f); await Conflict(() => Confirm(f, draft)); Assert.Equal(snapshot, await Snapshot(f));
    }

    private sealed class HeaderBarrier : DbCommandInterceptor
    {
        private int arrivals; private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("FROM [PurchaseReceipts] WITH (UPDLOCK, HOLDLOCK)"))
            { if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult(); await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), ct); }
            return result;
        }
    }
    private sealed class StockFailure(string stage) : DbCommandInterceptor
    {
        public int BalancesWritten; public int LedgersWritten;
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if ((stage == "secondBalance" && BalancesWritten == 1 && command.CommandText.Contains("UPDATE [InventoryBalances]"))
                || (stage == "ledger" && command.CommandText.Contains("INSERT INTO [InventoryTransactions]")))
                throw new InvalidOperationException("Test-only stock failure.");
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("UPDATE [InventoryBalances]")) BalancesWritten++;
            if (command.CommandText.Contains("INSERT INTO [InventoryTransactions]")) LedgersWritten++;
            return ValueTask.FromResult(result);
        }
    }
    private sealed class HeaderFailure : SaveChangesInterceptor
    {
        public bool FailedAfterSave;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<PurchaseReceipt>().Any(x => x.Entity.Status == DocumentStatus.Confirmed))
            { FailedAfterSave = true; throw new InvalidOperationException("Test-only failure after header UPDATE before commit."); }
            return ValueTask.FromResult(result);
        }
    }
}
