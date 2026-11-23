using BttMiniERP.Api.Data;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.DTOs.Reports;
using BttMiniERP.Api.Enums;
using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Api.Services;

public sealed class ReportService(ApplicationDbContext db)
{
    public Task<ReportPage<PurchaseRow>> PurchasesAsync(PurchaseReportQuery query, CancellationToken ct)
        => PageAsync(Purchases(db.PurchaseReceipts, query), query, ct);

    internal static IQueryable<PurchaseRow> Purchases(IQueryable<PurchaseReceipt> documents, PurchaseReportQuery query)
    {
        var (start, end) = query.Bounds();
        documents = documents.Where(x => x.Status == DocumentStatus.Confirmed
            && x.ConfirmedAt >= start && x.ConfirmedAt < end);
        if (query.SupplierId.HasValue) documents = documents.Where(x => x.SupplierId == query.SupplierId);
        if (query.ItemId.HasValue) documents = documents.Where(x => x.Lines.Any(line => line.ItemId == query.ItemId));

        var rows = from document in documents
                   group document by new { Date = DateOnly.FromDateTime(document.ConfirmedAt!.Value.Date), document.SupplierId } into g
                   orderby g.Key.Date, g.Key.SupplierId
                   select new PurchaseRow(g.Key.Date, g.Key.SupplierId, g.LongCount(), g.Sum(x => x.TotalAmount));
        return rows;
    }

    public Task<ReportPage<SalesRow>> SalesAsync(SalesReportQuery query, CancellationToken ct)
        => PageAsync(Sales(db.SalesOrders, query), query, ct);

    internal static IQueryable<SalesRow> Sales(IQueryable<SalesOrder> documents, SalesReportQuery query)
    {
        var (start, end) = query.Bounds();
        documents = documents.Where(x => x.Status == DocumentStatus.Confirmed
            && x.ConfirmedAt >= start && x.ConfirmedAt < end);
        if (query.CustomerId.HasValue) documents = documents.Where(x => x.CustomerId == query.CustomerId);
        if (query.ItemId.HasValue) documents = documents.Where(x => x.Lines.Any(line => line.ItemId == query.ItemId));

        var rows = from document in documents
                   group document by new { Date = DateOnly.FromDateTime(document.ConfirmedAt!.Value.Date), document.CustomerId } into g
                   orderby g.Key.Date, g.Key.CustomerId
                   select new SalesRow(g.Key.Date, g.Key.CustomerId, g.LongCount(), g.Sum(x => x.TotalAmount));
        return rows;
    }

    public Task<ReportPage<ProductionRow>> ProductionAsync(ProductionReportQuery query, CancellationToken ct)
        => PageAsync(Production(db.ProductionOrders, db.Items, query), query, ct);

    internal static IQueryable<ProductionRow> Production(IQueryable<ProductionOrder> orders, IQueryable<Item> items, ProductionReportQuery query)
    {
        var (start, end) = query.Bounds();
        orders = orders.Where(x => x.Status == DocumentStatus.Confirmed
            && x.ConfirmedAt >= start && x.ConfirmedAt < end);
        if (query.FinishedProductId.HasValue) orders = orders.Where(x => x.FinishedProductId == query.FinishedProductId);

        var rows = from order in orders
                   join item in items on order.FinishedProductId equals item.Id
                   group order by new { Date = DateOnly.FromDateTime(order.ConfirmedAt!.Value.Date), order.FinishedProductId, item.Unit } into g
                   orderby g.Key.Date, g.Key.FinishedProductId
                   select new ProductionRow(g.Key.Date, g.Key.FinishedProductId, g.Key.Unit, g.LongCount(), g.Sum(x => x.Quantity));
        return rows;
    }

    public Task<ReportPage<InventoryReportRow>> InventoryAsync(InventoryReportQuery query, CancellationToken ct)
        => PageAsync(Inventory(db.InventoryTransactions, db.Items, query), query, ct);

    internal static IQueryable<InventoryReportRow> Inventory(IQueryable<InventoryTransaction> entries, IQueryable<Item> items, InventoryReportQuery query)
    {
        var (start, end) = query.Bounds();
        if (query.ItemId.HasValue) items = items.Where(x => x.Id == query.ItemId);
        if (query.ItemType.HasValue) items = items.Where(x => x.ItemType == query.ItemType);
        var rows = from entry in entries
                   where entry.OccurredAt < end
                   join item in items on entry.ItemId equals item.Id
                   group entry by new { entry.ItemId, item.Unit } into g
                   orderby g.Key.ItemId
                   select new InventoryReportRow(g.Key.ItemId, g.Key.Unit,
                       g.Sum(x => x.OccurredAt < start ? x.QuantityDelta : 0m),
                       g.Sum(x => x.OccurredAt >= start && x.QuantityDelta > 0 ? x.QuantityDelta : 0m),
                       g.Sum(x => x.OccurredAt >= start && x.QuantityDelta < 0 ? -x.QuantityDelta : 0m),
                       g.Sum(x => x.QuantityDelta));
        return rows;
    }

    private static async Task<ReportPage<T>> PageAsync<T>(IQueryable<T> rows, ReportPeriod query, CancellationToken ct)
    {
        var count = await rows.CountAsync(ct);
        var page = await rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(ct);
        return new(page, query.Page, query.PageSize, count);
    }
}
