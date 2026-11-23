using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.DTOs.Inventory;
using BttMiniERP.Api.Enums;
using BttMiniERP.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BttMiniERP.Tests.Integration.Inventory;

[Collection("MasterData SQL"), Trait("Category", "SqlServer"), Trait("SqlSuite", "InventoryQuery")]
public sealed class InventoryQueryTests
{
    [InventoryQuerySqlFact]
    public async Task ViewerCanReconcileConfirmedMovementsAndFollowTheirSourceReferences()
    {
        // Opt-in riêng cho P6.1 và guard database SalesConfirmTest của fixture.
        Assert.Equal("1", Environment.GetEnvironmentVariable("BTT_RUN_SALES_CONFIRM_SQL_TESTS"));
        await using var fixture = await MasterDataSqlFixture.OpenAsync("Sales");
        await using var setup = fixture.Context();
        var category = await fixture.Categories(setup).CreateAsync(new() { Code = "INV", Name = "Inventory" }, default);
        async Task<int> Item(string code, string name, ItemType type) => (await fixture.Items(setup).CreateAsync(
            new() { CategoryId = category.Id, Code = code, Name = name, Unit = "kg", ItemType = type }, default)).Id;
        int raw = await Item("MAT", "Material", ItemType.RawMaterial);
        int product = await Item("FG", "Finished", ItemType.FinishedProduct);
        var supplier = await fixture.Suppliers(setup).CreateAsync(new() { Code = "SUP", Name = "Supplier" }, default);
        var buyer = await fixture.Customers(setup).CreateAsync(new() { Code = "CUS", Name = "Customer" }, default);

        await using var factory = new QueryFactory(fixture);
        factory.UseKestrel(0);
        using var client = factory.CreateClient();
        Assert.True(client.BaseAddress!.IsLoopback);
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token("Viewer"));
        async Task<InventoryPage<TransactionRow>> Ledger(string query = "") =>
            (await client.GetFromJsonAsync<InventoryPage<TransactionRow>>("/api/v1/inventory/transactions" + query))!;
        async Task<InventoryPage<BalanceRow>> Balances(string query = "") =>
            (await client.GetFromJsonAsync<InventoryPage<BalanceRow>>("/api/v1/inventory/balances" + query))!;

        var receipt = await fixture.Receipts(setup).CreateAsync(new() { SupplierId = supplier.Id,
            Lines = [new() { ItemId = raw, Quantity = 6, UnitPrice = 2 }] }, default);
        Assert.Empty((await Ledger()).Items);
        Assert.All((await Balances()).Items, x => Assert.Equal(0m, x.QuantityOnHand));
        await fixture.Receipts(setup).ConfirmAsync(receipt.Id, receipt.RowVersion, default);
        var bom = await fixture.Boms(setup).CreateAsync(new() { Code = "BOM", FinishedProductId = product,
            Details = [new() { RawMaterialId = raw, QuantityPerUnit = 2 }] }, default);
        var production = await fixture.Production(setup).CreateAsync(new() { BomId = bom.Id, Quantity = 3 }, default);
        Assert.Single((await Ledger()).Items);
        // Cố định đồng hồ của lần confirm để hai dòng kho nằm cùng thời điểm, hai phía ranh giới trang.
        var postingClock = new PostingClock(DateTimeOffset.UtcNow);
        await fixture.Production(setup, postingClock).ConfirmAsync(production.Id, production.RowVersion, default);
        var sale = await fixture.Sales(setup).CreateAsync(new() { CustomerId = buyer.Id,
            Lines = [new() { ItemId = product, Quantity = 2, UnitPrice = 4 }] }, default);
        Assert.Equal(3, (await Ledger()).TotalCount);
        await fixture.Sales(setup).ConfirmAsync(sale.Id, sale.RowVersion, default);

        var balances = await Balances("?sortBy=code");
        Assert.Equal(new[] { product, raw }, balances.Items.Select(x => x.ItemId));
        Assert.Equal(new[] { 1m, 0m }, balances.Items.Select(x => x.QuantityOnHand));
        var onlyRaw = await Balances($"?itemId={raw}&itemType=RawMaterial&search=ateri");
        Assert.Equal(raw, Assert.Single(onlyRaw.Items).ItemId);
        Assert.Empty((await Balances("?search=unmatched")).Items);
        Assert.Equal(product, Assert.Single((await Balances("?page=2&pageSize=1")).Items).ItemId);

        var all = await Ledger("?sortBy=id&sortDirection=asc");
        Assert.Equal(4, all.TotalCount);
        Assert.Equal(new[] { 6m, -6m, 3m, -2m }, all.Items.Select(x => x.QuantityDelta));
        Assert.Equal(new[] { 6m, 0m, 3m, 1m }, all.Items.Select(x => x.BalanceAfter));
        Assert.All(all.Items, x => Assert.Equal(fixture.ActorId, x.PerformedByUserId));
        foreach (var b in balances.Items)
            Assert.Equal(b.QuantityOnHand, all.Items.Where(x => x.ItemId == b.ItemId).Sum(x => x.QuantityDelta));
        Assert.Equal(new[] { receipt.Lines.Single().Id, production.Materials.Single().Id, 0, sale.Lines.Single().Id },
            all.Items.Select(x => x.SourceLineId));
        Assert.Equal(new[] { $"/api/v1/purchase-receipts/{receipt.Id}", $"/api/v1/production-orders/{production.Id}",
            $"/api/v1/production-orders/{production.Id}", $"/api/v1/sales-orders/{sale.Id}" }, all.Items.Select(x => x.SourceDocumentUrl));
        // Kiểm cả URL trong JSON thực tế; DTO phía client cũng tự tính thuộc tính này.
        var wire = await client.GetFromJsonAsync<JsonElement>("/api/v1/inventory/transactions?sortBy=id&sortDirection=asc");
        var urls = wire.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("sourceDocumentUrl").GetString()).ToArray();
        Assert.Equal(all.Items.Select(x => x.SourceDocumentUrl), urls);
        foreach (var url in urls.Distinct())
        {
            using var forbidden = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        await using var verify = fixture.Context();
        var storedBalances = await verify.InventoryBalances.AsNoTracking().ToDictionaryAsync(x => x.ItemId, x => x.QuantityOnHand);
        Assert.Equal(storedBalances.Count, balances.Items.Count);
        Assert.All(balances.Items, x => Assert.Equal(storedBalances[x.ItemId], x.QuantityOnHand));
        var entries = await verify.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        Assert.Equal(new[] { InventoryTransactionType.PurchaseReceipt, InventoryTransactionType.ProductionConsumption,
            InventoryTransactionType.ProductionOutput, InventoryTransactionType.SalesIssue }, entries.Select(x => x.TransactionType));
        Assert.Equal(entries.Select(x => (x.Id, x.ItemId, x.TransactionType, x.QuantityDelta, x.BalanceAfter, x.SourceDocumentType,
            x.SourceDocumentId, x.SourceLineId, x.PerformedByUserId, x.OccurredAt)),
            all.Items.Select(x => (x.Id, x.ItemId, x.TransactionType, x.QuantityDelta, x.BalanceAfter, x.SourceDocumentType,
            x.SourceDocumentId, x.SourceLineId, x.PerformedByUserId, x.OccurredAt)));
        var filtered = await Ledger($"?sourceDocumentType=SalesOrder&sourceDocumentId={sale.Id}&transactionType=SalesIssue&itemType=FinishedProduct&itemId={product}");
        Assert.Equal(all.Items[3], Assert.Single(filtered.Items));
        Assert.Empty((await Ledger($"?itemId={raw}&transactionType=SalesIssue")).Items);

        var newest = all.Items.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).ToArray();
        Assert.Equal(postingClock.GetUtcNow(), newest[1].OccurredAt);
        Assert.Equal(newest[1].OccurredAt, newest[2].OccurredAt);
        Assert.True(newest[1].Id > newest[2].Id);
        Assert.Equal(newest.Take(2), (await Ledger("?pageSize=2")).Items);
        var second = await Ledger("?page=2&pageSize=2");
        Assert.Equal(4, second.TotalCount);
        Assert.Equal(newest.Skip(2), second.Items);
        Assert.Empty((await Ledger("?page=3&pageSize=2")).Items);
        var instant = newest[0].OccurredAt;
        string Time(DateTimeOffset value) => Uri.EscapeDataString(value.ToOffset(TimeSpan.FromHours(7)).ToString("O"));
        Assert.Equal(newest.Where(x => x.OccurredAt == instant), (await Ledger($"?from={Time(instant)}&to={Time(instant.AddTicks(1))}")).Items);
        Assert.Equal(newest.Where(x => x.OccurredAt < instant), (await Ledger($"?to={Time(instant)}")).Items);

        foreach (var endpoint in new[] { "balances", "transactions" })
        {
            var path = "/api/v1/inventory/" + endpoint;
            client.DefaultRequestHeaders.Authorization = null;
            using var anonymous = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token());
            using var noRole = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, noRole.StatusCode);
            foreach (var role in new[] { "Admin", "Manager", "PurchasingStaff", "SalesStaff", "ProductionStaff", "WarehouseStaff", "Viewer" })
            {
                client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token(role));
                using var allowed = await client.GetAsync(path + "?pageSize=100");
                Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            }
            using var invalid = await client.GetAsync(path + "?pageSize=101");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType!.MediaType);
            var problem = await invalid.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(400, problem.GetProperty("status").GetInt32());
        }
    }

    private sealed class PostingClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }

    private sealed class QueryFactory(MasterDataSqlFixture fixture) : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(s => s.AddScoped<ApplicationDbContext>(_ => fixture.Context()));
        }
    }
}

public sealed class InventoryQuerySqlFactAttribute : FactAttribute
{
    public InventoryQuerySqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BTT_RUN_INVENTORY_QUERY_SQL_TESTS") != "1")
            Skip = "P6.1 cần phê duyệt riêng cho việc tạo và dọn dữ liệu chứng từ kiểm thử.";
    }
}
