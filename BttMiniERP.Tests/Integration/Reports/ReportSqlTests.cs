using System.Net.Http.Json;
using System.Text.Json;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.DTOs.Inventory;
using BttMiniERP.Api.DTOs.Reports;
using BttMiniERP.Api.Enums;
using BttMiniERP.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace BttMiniERP.Tests.Integration.Reports;

[Collection("MasterData SQL"), Trait("Category", "SqlServer"), Trait("SqlSuite", "Reports")]
public sealed class ReportSqlTests(ITestOutputHelper output)
{
    [ReportSqlFact]
    public async Task ReconcileConfirmedDocumentsAndHistoricalLedgerThroughHttp()
    {
        Assert.Equal("1", Environment.GetEnvironmentVariable("BTT_RUN_SALES_CONFIRM_SQL_TESTS"));
        await using var fixture = await MasterDataSqlFixture.OpenAsync("Sales");
        await using var db = fixture.Context();
        var category = await fixture.Categories(db).CreateAsync(new() { Code = "REPORT", Name = "Report" }, default);
        async Task<int> Item(string code, string unit, ItemType type) =>
            (await fixture.Items(db).CreateAsync(new() { Code = code, Name = code, CategoryId = category.Id,
                Unit = unit, ItemType = type }, default)).Id;
        int raw = await Item("RAW", "kg", ItemType.RawMaterial);
        int fabric = await Item("FABRIC", "m", ItemType.RawMaterial);
        int product = await Item("FG", "box", ItemType.FinishedProduct);
        int roll = await Item("ROLL", "roll", ItemType.FinishedProduct);
        async Task<int> Supplier(string code) => (await fixture.Suppliers(db).CreateAsync(new() { Code = code, Name = code }, default)).Id;
        async Task<int> Customer(string code) => (await fixture.Customers(db).CreateAsync(new() { Code = code, Name = code }, default)).Id;
        int s1 = await Supplier("S1"), s2 = await Supplier("S2");
        int c1 = await Customer("C1"), c2 = await Customer("C2");
        async Task<int> Bom(string code, int finished, int material) => (await fixture.Boms(db).CreateAsync(new()
            { Code = code, FinishedProductId = finished, Details = [new() { RawMaterialId = material, QuantityPerUnit = 1 }] }, default)).Id;
        int firstBom = await Bom("B1", product, raw), secondBom = await Bom("B2", roll, fabric);

        var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new ReportClock(start.AddDays(-2));
        var purchases = fixture.Receipts(db, clock);
        var production = fixture.Production(db, clock);
        var sales = fixture.Sales(db, clock);
        async Task Purchase(int supplier, int item, decimal qty, decimal price, int day, DocumentStatus state = DocumentStatus.Confirmed)
        {
            clock.Instant = start.AddDays(-2);
            var receipt = await purchases.CreateAsync(new() { SupplierId = supplier,
                Lines = [new() { ItemId = item, Quantity = qty, UnitPrice = price }] }, default);
            clock.Instant = start.AddDays(day);
            if (state == DocumentStatus.Confirmed) await purchases.ConfirmAsync(receipt.Id, receipt.RowVersion, default);
            if (state == DocumentStatus.Cancelled) await purchases.CancelAsync(receipt.Id, receipt.RowVersion, default);
        }
        await Purchase(s1, raw, 10, 1, -1);
        await Purchase(s1, raw, 1.005m, 1, 0);
        await Purchase(s1, raw, 2, 3, 0);
        await Purchase(s2, fabric, 5, 4, 1);
        await Purchase(s1, raw, 10, 1, 0, DocumentStatus.Draft);
        await Purchase(s1, raw, 10, 1, 0, DocumentStatus.Cancelled);

        async Task Produce(int bom, int qty, int day, DocumentStatus state)
        {
            clock.Instant = start.AddDays(-2);
            var order = await production.CreateAsync(new() { BomId = bom, Quantity = qty }, default);
            clock.Instant = start.AddDays(day);
            if (state == DocumentStatus.Confirmed) await production.ConfirmAsync(order.Id, order.RowVersion, default);
            if (state == DocumentStatus.Cancelled) await production.CancelAsync(order.Id, order.RowVersion, default);
        }
        await Produce(firstBom, 3, 0, DocumentStatus.Confirmed);
        await Produce(secondBom, 4, 1, DocumentStatus.Confirmed);
        await Produce(firstBom, 2, 0, DocumentStatus.Draft);
        await Produce(firstBom, 1, 0, DocumentStatus.Cancelled);

        async Task Sale(int buyer, int item, decimal qty, int day, DocumentStatus state)
        {
            clock.Instant = start.AddDays(-2);
            var order = await sales.CreateAsync(new() { CustomerId = buyer,
                Lines = [new() { ItemId = item, Quantity = qty, UnitPrice = 2 }] }, default);
            clock.Instant = start.AddDays(day);
            if (state == DocumentStatus.Confirmed) await sales.ConfirmAsync(order.Id, order.RowVersion, default);
            if (state == DocumentStatus.Cancelled) await sales.CancelAsync(order.Id, order.RowVersion, default);
        }
        await Sale(c1, product, 1.125m, 0, DocumentStatus.Confirmed);
        await Sale(c2, roll, 2, 1, DocumentStatus.Confirmed);
        await Sale(c1, product, 1, 0, DocumentStatus.Draft);
        await Sale(c1, product, 1, 0, DocumentStatus.Cancelled);
        // Những lần confirm đúng mốc cuối chỉ thuộc kỳ kế tiếp.
        await Purchase(s2, fabric, 1, 4, 2);
        await Produce(secondBom, 1, 2, DocumentStatus.Confirmed);
        await Sale(c2, roll, 1, 2, DocumentStatus.Confirmed);

        await using var verify = fixture.Context();
        var receipts = await verify.PurchaseReceipts.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.CreatedByUserId == fixture.ActorId).ToListAsync();
        var orders = await verify.SalesOrders.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.CreatedByUserId == fixture.ActorId).ToListAsync();
        var jobs = await verify.ProductionOrders.AsNoTracking().Include(x => x.Materials)
            .Where(x => x.CreatedByUserId == fixture.ActorId).ToListAsync();
        var movements = await verify.InventoryTransactions.AsNoTracking().Where(x => x.PerformedByUserId == fixture.ActorId).ToListAsync();
        var detailIds = await verify.BillOfMaterialDetails.Where(x => x.BillOfMaterialId == firstBom || x.BillOfMaterialId == secondBom)
            .Select(x => x.Id).ToListAsync();
        var balanceIds = await verify.InventoryBalances.Where(x => x.CreatedByUserId == fixture.ActorId).Select(x => x.ItemId).ToListAsync();
        output.WriteLine(JsonSerializer.Serialize(new { Actor = fixture.ActorId, Category = category.Id, Items = new[] { raw, fabric, product, roll },
            Suppliers = new[] { s1, s2 }, Customers = new[] { c1, c2 }, Boms = new[] { firstBom, secondBom }, BomDetails = detailIds,
            Receipts = receipts.Select(x => x.Id), ReceiptLines = receipts.SelectMany(x => x.Lines).Select(x => x.Id),
            Sales = orders.Select(x => x.Id), SalesLines = orders.SelectMany(x => x.Lines).Select(x => x.Id),
            Production = jobs.Select(x => x.Id), Materials = jobs.SelectMany(x => x.Materials).Select(x => x.Id),
            Ledger = movements.Select(x => x.Id), Balances = balanceIds }));
        var end = start.AddDays(2);
        bool InPeriod(DocumentStatus state, DateTimeOffset? at) => state == DocumentStatus.Confirmed && at >= start && at < end;
        foreach (var timestamps in new[] { receipts.Select(x => x.ConfirmedAt), orders.Select(x => x.ConfirmedAt), jobs.Select(x => x.ConfirmedAt) })
        {
            Assert.Contains(start, timestamps);
            Assert.Contains(end, timestamps);
        }
        Assert.All(receipts, x => Assert.Equal(x.TotalAmount, x.Lines.Sum(l => l.LineTotal)));
        Assert.All(orders, x => Assert.Equal(x.TotalAmount, x.Lines.Sum(l => l.LineTotal)));
        foreach (var job in jobs.Where(x => x.Status == DocumentStatus.Confirmed))
        {
            var snapshot = Assert.Single(job.Materials);
            Assert.Equal(job.Quantity * snapshot.QuantityPerUnitSnapshot, snapshot.ActualQuantity);
        }

        await using var factory = new ReportFactory(fixture);
        factory.UseKestrel(0);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token("Viewer"));
        const string period = "?from=2026-11-01T00:00:00Z&to=2026-11-03T00:00:00Z";
        const string next = "?from=2026-11-03T00:00:00Z&to=2026-11-04T00:00:00Z";
        const string prefix = "/api/v1/reports/";
        async Task<T[]> Rows<T>(string report, string query = period) =>
            (await client.GetFromJsonAsync<ReportPage<T>>(prefix + report + query))!.Items.ToArray();
        DateOnly day1 = new(2026, 11, 1), day2 = day1.AddDays(1);
        Assert.Equal(new[] { new PurchaseRow(day1, s1, 2, 7.01m), new PurchaseRow(day2, s2, 1, 20) }, await Rows<PurchaseRow>("purchases"));
        Assert.Equal(new[] { new SalesRow(day1, c1, 1, 2.25m), new SalesRow(day2, c2, 1, 4) }, await Rows<SalesRow>("sales"));
        Assert.Equal(new[] { new ProductionRow(day1, product, "box", 1, 3), new ProductionRow(day2, roll, "roll", 1, 4) },
            await Rows<ProductionRow>("production"));
        var inventory = await Rows<InventoryReportRow>("inventory");
        var purchased = receipts.Where(x => InPeriod(x.Status, x.ConfirmedAt))
            .GroupBy(x => (Date: DateOnly.FromDateTime(x.ConfirmedAt!.Value.UtcDateTime), x.SupplierId)).OrderBy(g => g.Key)
            .Select(g => new PurchaseRow(g.Key.Date, g.Key.SupplierId, g.LongCount(), g.Sum(x => x.TotalAmount)));
        var sold = orders.Where(x => InPeriod(x.Status, x.ConfirmedAt))
            .GroupBy(x => (Date: DateOnly.FromDateTime(x.ConfirmedAt!.Value.UtcDateTime), x.CustomerId)).OrderBy(g => g.Key)
            .Select(g => new SalesRow(g.Key.Date, g.Key.CustomerId, g.LongCount(), g.Sum(x => x.TotalAmount)));
        var units = await verify.Items.Where(x => x.CreatedByUserId == fixture.ActorId).ToDictionaryAsync(x => x.Id, x => x.Unit);
        var produced = jobs.Where(x => InPeriod(x.Status, x.ConfirmedAt))
            .GroupBy(x => (Date: DateOnly.FromDateTime(x.ConfirmedAt!.Value.UtcDateTime), x.FinishedProductId)).OrderBy(g => g.Key)
            .Select(g => new ProductionRow(g.Key.Date, g.Key.FinishedProductId, units[g.Key.FinishedProductId], g.LongCount(), g.Sum(x => x.Quantity)));
        Assert.Equal(purchased, await Rows<PurchaseRow>("purchases"));
        Assert.Equal(sold, await Rows<SalesRow>("sales"));
        Assert.Equal(produced, await Rows<ProductionRow>("production"));
        output.WriteLine(JsonSerializer.Serialize(new { purchased, sold, produced, inventory, start, end }));
        Assert.Equal(new[] { new InventoryReportRow(raw, "kg", 10, 3.005m, 3, 10.005m),
            new InventoryReportRow(fabric, "m", 0, 5, 4, 1), new InventoryReportRow(product, "box", 0, 3, 1.125m, 1.875m),
            new InventoryReportRow(roll, "roll", 0, 4, 2, 2) }, inventory);
        var ledger = (await client.GetFromJsonAsync<InventoryPage<TransactionRow>>("/api/v1/inventory/transactions?to=2026-11-03T00:00:00Z&pageSize=100"))!;
        Assert.Equal(4, ledger.Items.Select(x => x.TransactionType).Distinct().Count());
        foreach (var row in inventory)
        {
            var history = movements.Where(x => x.ItemId == row.ItemId && x.OccurredAt < end).ToList();
            Assert.Equal(history.Where(x => x.OccurredAt < start).Sum(x => x.QuantityDelta), row.Opening);
            Assert.Equal(history.Where(x => x.OccurredAt >= start && x.QuantityDelta > 0).Sum(x => x.QuantityDelta), row.Received);
            Assert.Equal(-history.Where(x => x.OccurredAt >= start && x.QuantityDelta < 0).Sum(x => x.QuantityDelta), row.Issued);
            Assert.Equal(row.Closing, row.Opening + row.Received - row.Issued);
            Assert.Equal(row.Closing, ledger.Items.Where(x => x.ItemId == row.ItemId).Sum(x => x.QuantityDelta));
        }

        Assert.Equal(4, Assert.Single(await Rows<PurchaseRow>("purchases", next)).TotalAmount);
        Assert.Equal(2, Assert.Single(await Rows<SalesRow>("sales", next)).Revenue);
        Assert.Equal(1, Assert.Single(await Rows<ProductionRow>("production", next)).Quantity);
        var balances = (await client.GetFromJsonAsync<InventoryPage<BalanceRow>>("/api/v1/inventory/balances"))!;
        foreach (var row in await Rows<InventoryReportRow>("inventory", next))
            Assert.Equal(balances.Items.Single(b => b.ItemId == row.ItemId).QuantityOnHand, row.Closing);
        foreach (var report in new[] { "purchases", "sales", "inventory", "production" })
        {
            var empty = await client.GetFromJsonAsync<JsonElement>(prefix + report + "?from=2025-01-01T00:00Z&to=2025-02-01T00:00Z");
            Assert.Empty(empty.GetProperty("items").EnumerateArray());
            Assert.Equal(0, empty.GetProperty("totalCount").GetInt32());
        }
        var wire = await client.GetFromJsonAsync<JsonElement>(prefix + "sales" + period + "&pageSize=1&page=2");
        Assert.Equal(2, wire.GetProperty("totalCount").GetInt32());
        var line = Assert.Single(wire.GetProperty("items").EnumerateArray());
        Assert.Equal(c2, line.GetProperty("customerId").GetInt32());
        Assert.Equal(new[] { "customerId", "date", "documentCount", "revenue" }, line.EnumerateObject().Select(p => p.Name).Order());

        Assert.Equal(7.01m, Assert.Single(await Rows<PurchaseRow>("purchases", period + $"&itemId={raw}&supplierId={s1}")).TotalAmount);
        Assert.Empty(await Rows<SalesRow>("sales", period + $"&customerId={c2}&itemId={product}"));
        Assert.Equal(4, Assert.Single(await Rows<ProductionRow>("production", period + $"&finishedProductId={roll}")).Quantity);
        Assert.Equal(new[] { product, roll }, (await Rows<InventoryReportRow>("inventory", period + "&itemType=FinishedProduct")).Select(x => x.ItemId));
        foreach (var role in new string?[] { null, "Admin", "Manager", "Viewer", "WarehouseStaff", "PurchasingStaff", "SalesStaff", "ProductionStaff" })
        {
            client.DefaultRequestHeaders.Authorization = role == null ? null : new("Bearer", factory.Token(role));
            foreach (var report in new[] { "purchases", "sales", "inventory", "production" })
            {
                using var response = await client.GetAsync(prefix + report + period);
                bool allowed = role is "Admin" or "Manager" or "Viewer" || role == "WarehouseStaff" && report == "inventory";
                Assert.Equal(role == null ? System.Net.HttpStatusCode.Unauthorized : allowed
                    ? System.Net.HttpStatusCode.OK : System.Net.HttpStatusCode.Forbidden, response.StatusCode);
                output.WriteLine($"{role ?? "Anonymous"} {report} {(int)response.StatusCode}");
                if (allowed)
                {
                    using var invalid = await client.GetAsync(prefix + report + period + "&pageSize=101");
                    Assert.Equal(System.Net.HttpStatusCode.BadRequest, invalid.StatusCode);
                    Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType?.MediaType);
                    Assert.Equal(400, (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetInt32());
                }
            }
        }
    }

    private sealed class ReportClock(DateTimeOffset initial) : TimeProvider
    {
        public DateTimeOffset Instant { get; set; } = initial;
        public override DateTimeOffset GetUtcNow() => Instant;
    }

    private sealed class ReportFactory(MasterDataSqlFixture fixture) : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(s => s.AddScoped<ApplicationDbContext>(_ => fixture.Context()));
        }
    }
}

public sealed class ReportSqlFactAttribute : FactAttribute
{
    public ReportSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BTT_RUN_REPORT_SQL_TESTS") != "1")
            Skip = "P6.2 cần phê duyệt riêng cho chứng từ và ledger tạm.";
    }
}
