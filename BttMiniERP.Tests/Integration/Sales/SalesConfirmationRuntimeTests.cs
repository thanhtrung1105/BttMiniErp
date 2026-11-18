using System.Net;
using System.Net.Http.Json;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.DTOs.SalesOrders;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
using BttMiniERP.Api.Services;
using BttMiniERP.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BttMiniERP.Tests.Integration.Sales;

public sealed partial class SalesOrdersSqlTests
{
    [SalesSqlFact]
    public async Task KestrelConfirmsFinishedOutputOnceAndReturnsConflictWhenStockRunsOut()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Sales");
        var (customer, product) = await Setup(f);
        await using var db = f.Context();
        var raw = await f.Items(db).CreateAsync(new() { Code = "RAW", Name = "Material", Unit = "kg",
            CategoryId = await db.Categories.Select(x => x.Id).SingleAsync(), ItemType = ItemType.RawMaterial }, default);
        var supplier = await f.Suppliers(db).CreateAsync(new() { Code = "SUP", Name = "Supplier" }, default);
        var receipt = await f.Receipts(db).CreateAsync(new() { SupplierId = supplier.Id,
            Lines = [new() { ItemId = raw.Id, Quantity = 3, UnitPrice = 2 }] }, default);
        await f.Receipts(db).ConfirmAsync(receipt.Id, receipt.RowVersion, default);
        var bom = await f.Boms(db).CreateAsync(new() { Code = "BOM", FinishedProductId = product,
            Details = [new() { RawMaterialId = raw.Id, QuantityPerUnit = 1 }] }, default);
        var production = await f.Production(db).CreateAsync(new() { BomId = bom.Id, Quantity = 3 }, default);
        await f.Production(db).ConfirmAsync(production.Id, production.RowVersion, default);
        var request = new CreateSalesOrderRequest { CustomerId = customer, Lines = [new() { ItemId = product, Quantity = 2, UnitPrice = 5 }] };
        var first = await f.Sales(db).CreateAsync(request, default);
        var second = await f.Sales(db).CreateAsync(request, default);

        await using var factory = new SalesRuntimeFactory(f);
        factory.UseKestrel(0);
        using var client = factory.CreateClient();
        Assert.True(client.BaseAddress!.IsLoopback);
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.WarehouseToken());
        Task<HttpResponseMessage> Post(SalesOrderResponse order) => client.PostAsJsonAsync(
            $"/api/v1/sales-orders/{order.Id}/confirm", new { order.RowVersion });
        using var success = await Post(first);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        var confirmed = await success.Content.ReadFromJsonAsync<SalesOrderResponse>();
        Assert.NotNull(confirmed);
        Assert.Equal(DocumentStatus.Confirmed, confirmed.Status);
        foreach (var order in new[] { confirmed, second })
        {
            using var rejected = await Post(order);
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
            Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType!.MediaType);
        }
        await using var verify = f.Context();
        Assert.Equal(1m, (await verify.InventoryBalances.SingleAsync(x => x.ItemId == product)).QuantityOnHand);
        var issue = await verify.InventoryTransactions.SingleAsync(x => x.TransactionType == InventoryTransactionType.SalesIssue);
        Assert.Equal(first.Id, issue.SourceDocumentId);
        Assert.Equal(f.ActorId, issue.PerformedByUserId);
        Assert.Equal(DocumentStatus.Draft, (await f.Sales(verify).GetAsync(second.Id, default)).Status);
    }

    private sealed class SalesRuntimeFactory(MasterDataSqlFixture fixture) : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services => services.AddScoped<ApplicationDbContext>(_ => fixture.Context()));
        }

        public string WarehouseToken() => new TokenService(Options.Create(Jwt), TimeProvider.System)
            .Create(new ApplicationUser { Id = fixture.ActorId, UserName = fixture.ActorId }, ["WarehouseStaff"]).AccessToken;
    }
}
