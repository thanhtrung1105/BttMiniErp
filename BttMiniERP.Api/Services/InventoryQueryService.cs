using BttMiniERP.Api.Data;
using BttMiniERP.Api.DTOs.Inventory;
using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Api.Services;

public sealed class InventoryQueryService(ApplicationDbContext db)
{
    public async Task<InventoryPage<BalanceRow>> BalancesAsync(InventoryBalanceQuery query, CancellationToken ct)
    {
        var rows = from balance in db.InventoryBalances.AsNoTracking()
                   join item in db.Items on balance.ItemId equals item.Id
                   select new { balance, item };
        if (query.ItemId.HasValue) rows = rows.Where(x => x.item.Id == query.ItemId);
        if (query.ItemType.HasValue) rows = rows.Where(x => x.item.ItemType == query.ItemType);
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
            rows = rows.Where(x => x.item.Code.Contains(search) || x.item.Name.Contains(search));

        var total = await rows.CountAsync(ct);
        var asc = query.SortDirection == "asc";
        var ordered = query.SortBy == "code"
            ? (asc ? rows.OrderBy(x => x.item.Code).ThenBy(x => x.item.Id)
                   : rows.OrderByDescending(x => x.item.Code).ThenByDescending(x => x.item.Id))
            : (asc ? rows.OrderBy(x => x.item.Id) : rows.OrderByDescending(x => x.item.Id));
        var items = await ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new BalanceRow(x.item.Id, x.item.Code, x.item.Name, x.item.ItemType, x.item.Unit,
                x.balance.QuantityOnHand)).ToArrayAsync(ct);
        return new(items, query.Page, query.PageSize, total);
    }

    public async Task<InventoryPage<TransactionRow>> TransactionsAsync(InventoryTransactionQuery query, CancellationToken ct)
    {
        var rows = from entry in db.InventoryTransactions.AsNoTracking()
                   join item in db.Items on entry.ItemId equals item.Id
                   select new { entry, item };
        if (query.ItemId.HasValue) rows = rows.Where(x => x.item.Id == query.ItemId);
        if (query.ItemType.HasValue) rows = rows.Where(x => x.item.ItemType == query.ItemType);
        if (query.SourceDocumentType.HasValue) rows = rows.Where(x => x.entry.SourceDocumentType == query.SourceDocumentType);
        if (query.SourceDocumentId.HasValue) rows = rows.Where(x => x.entry.SourceDocumentId == query.SourceDocumentId);
        if (query.TransactionType.HasValue) rows = rows.Where(x => x.entry.TransactionType == query.TransactionType);
        var from = query.From?.ToUniversalTime();
        var to = query.To?.ToUniversalTime();
        if (from.HasValue) rows = rows.Where(x => x.entry.OccurredAt >= from);
        if (to.HasValue) rows = rows.Where(x => x.entry.OccurredAt < to);

        var total = await rows.CountAsync(ct);
        var asc = query.SortDirection == "asc";
        var ordered = query.SortBy == "occurredAt"
            ? (asc ? rows.OrderBy(x => x.entry.OccurredAt).ThenBy(x => x.entry.Id)
                   : rows.OrderByDescending(x => x.entry.OccurredAt).ThenByDescending(x => x.entry.Id))
            : (asc ? rows.OrderBy(x => x.entry.Id) : rows.OrderByDescending(x => x.entry.Id));
        var items = await ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new TransactionRow(x.entry.Id, x.item.Id, x.item.Code, x.item.Name, x.item.ItemType,
                x.item.Unit, x.entry.TransactionType, x.entry.QuantityDelta, x.entry.BalanceAfter, x.entry.OccurredAt,
                x.entry.SourceDocumentType, x.entry.SourceDocumentId, x.entry.SourceLineId, x.entry.PerformedByUserId))
            .ToArrayAsync(ct);
        return new(items, query.Page, query.PageSize, total);
    }
}
