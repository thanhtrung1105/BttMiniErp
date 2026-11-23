using System.ComponentModel.DataAnnotations;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.DTOs.Reports;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
using BttMiniERP.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Tests.Unit.Reports;

public sealed class ReportTests
{
    private const string Start = "2026-11-01T07:00:00+07:00";
    private const string End = "2026-11-03T00:00:00Z";
    private static readonly DateTimeOffset First = DateTimeOffset.Parse(Start).ToUniversalTime();

    [Theory]
    [InlineData(null, End)]
    [InlineData(Start, null)]
    [InlineData(Start, Start)]
    [InlineData(End, Start)]
    [InlineData("2026-11-01T00:00:00", End)]
    [InlineData("bad-date", End)]
    [InlineData("2026-11-01T00:00:00+15:00", End)]
    public void InvalidPeriodIsRejected(string? from, string? to)
    {
        var period = new ReportPeriod { From = from, To = to };
        Assert.False(Validator.TryValidateObject(period, new(period), [], true));
    }

    [Fact]
    public void PurchaseTotalsCountWholeDocumentsOnceAndKeepStoredMoneyPrecision()
    {
        PurchaseReceipt Receipt(int day, decimal total, int supplier = 1, DocumentStatus status = DocumentStatus.Confirmed)
            => new() { SupplierId = supplier, Status = status, ConfirmedAt = First.AddDays(day), TotalAmount = total,
                Lines = [new() { ItemId = 3 }, new() { ItemId = 4 }] };
        var receipts = new[] { Receipt(-1, 3), Receipt(0, 0.01m), Receipt(0, 9999999999999999.99m),
            Receipt(0, 7, 2), Receipt(1, 2), Receipt(2, 8), Receipt(0, 55, status: DocumentStatus.Draft),
            Receipt(0, 99, status: DocumentStatus.Cancelled) }.AsQueryable();
        var query = new PurchaseReportQuery { From = Start, To = End, SupplierId = 1, ItemId = 3 };
        var rows = ReportService.Purchases(receipts, query).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal(new PurchaseRow(new(2026, 11, 1), 1, 2, 10000000000000000.00m), rows[0]);
        Assert.Equal(2m, rows[1].TotalAmount);
        Assert.Empty(ReportService.Purchases(receipts, new() { From = Start, To = End, ItemId = 99 }));
    }

    [Fact]
    public void SignedLedgerSeparatesItemsAndPreservesOpeningBalance()
    {
        InventoryTransaction Entry(int item, decimal delta, int day, InventoryTransactionType type)
            => new() { ItemId = item, QuantityDelta = delta, OccurredAt = First.AddDays(day), TransactionType = type };
        var entries = new[] { Entry(1, 10, -1, InventoryTransactionType.PurchaseReceipt),
            Entry(1, 0.125m, 0, InventoryTransactionType.PurchaseReceipt),
            Entry(1, -2.5m, 1, InventoryTransactionType.ProductionConsumption),
            Entry(2, 3, 0, InventoryTransactionType.ProductionOutput), Entry(2, -1, 0, InventoryTransactionType.SalesIssue),
            Entry(2, -2, 2, InventoryTransactionType.SalesIssue) }.AsQueryable();
        var items = new[] { new Item { Id = 1, Unit = "kg" }, new Item { Id = 2, Unit = "cái" } }.AsQueryable();
        var rows = ReportService.Inventory(entries, items, new() { From = Start, To = End }).ToArray();
        Assert.Equal(new InventoryReportRow(1, "kg", 10, 0.125m, 2.5m, 7.625m), rows[0]);
        Assert.Equal(new InventoryReportRow(2, "cái", 0, 3, 1, 2), rows[1]);
        Assert.Equal(2, rows.Length);
    }

    [Fact]
    public void SalesRevenueAndProductionUseUtcConfirmationDayAndExcludeUnpostedDocuments()
    {
        // ConfirmedAt do server ghi bằng GetUtcNow; from của client mang offset +07:00.
        var posted = First.AddHours(23.5);
        var orders = Enum.GetValues<DocumentStatus>().Select(status => new SalesOrder
        {
            CustomerId = 4, Status = status, ConfirmedAt = posted, TotalAmount = 7.11m
        }).AsQueryable();
        Assert.Equal(new SalesRow(new(2026, 11, 1), 4, 1, 7.11m),
            Assert.Single(ReportService.Sales(orders, new() { From = Start, To = End })));
        var jobs = new[] { new ProductionOrder { FinishedProductId = 2, Status = DocumentStatus.Confirmed, ConfirmedAt = posted, Quantity = 1.125m },
            new ProductionOrder { FinishedProductId = 2, Status = DocumentStatus.Draft, ConfirmedAt = posted, Quantity = 10 },
            new ProductionOrder { FinishedProductId = 3, Status = DocumentStatus.Confirmed, ConfirmedAt = posted, Quantity = 7 },
            new ProductionOrder { FinishedProductId = 3, Status = DocumentStatus.Cancelled, ConfirmedAt = posted, Quantity = 1 } }.AsQueryable();
        var items = new[] { new Item { Id = 2, Unit = "kg" }, new Item { Id = 3, Unit = "cái" } }.AsQueryable();
        Assert.Equal(new[] { new ProductionRow(new(2026, 11, 1), 2, "kg", 1, 1.125m),
            new ProductionRow(new(2026, 11, 1), 3, "cái", 1, 7) },
            ReportService.Production(jobs, items, new() { From = Start, To = End }).ToArray());
    }

    [Fact]
    public void AllFourPagedQueriesTranslateWithSqlServerProviderOffline()
    {
        using var db = new ApplicationDbContextFactory().CreateDbContext(["--offline"]);
        void Check<T>(IQueryable<T> query)
        {
            var sql = query.Skip(20).Take(20).ToQueryString();
            Assert.Contains("GROUP BY", sql);
            Assert.Contains("SUM(", sql);
            Assert.Contains("ORDER BY", sql);
            Assert.Contains("OFFSET", sql);
        }
        Check(ReportService.Purchases(db.PurchaseReceipts, new() { From = Start, To = End, ItemId = 3 }));
        Check(ReportService.Sales(db.SalesOrders, new() { From = Start, To = End, CustomerId = 1, ItemId = 2 }));
        Check(ReportService.Production(db.ProductionOrders, db.Items, new() { From = Start, To = End }));
        Check(ReportService.Inventory(db.InventoryTransactions, db.Items, new() { From = Start, To = End }));
    }
}
