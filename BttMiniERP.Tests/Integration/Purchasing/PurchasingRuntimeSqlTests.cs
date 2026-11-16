using System.Net;
using System.Net.Http.Json;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.DTOs.PurchaseReceipts;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
using BttMiniERP.Api.Services;
using BttMiniERP.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BttMiniERP.Tests.Integration.Purchasing;

[Collection("MasterData SQL"), Trait("Category", "SqlServer"), Trait("SqlSuite", "PurchasingRuntime")]
public sealed class PurchasingRuntimeSqlTests
{
    [PurchasingSqlFact]
    public async Task KestrelDraftLifecycleUsesSqlAndEnforcesValidationAuthorizationAndVersion()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Purchasing");
        await using var setup = f.Context();
        var supplier = await f.Suppliers(setup).CreateAsync(new() { Code = "HTTP", Name = "HTTP" }, default);
        var category = await f.Categories(setup).CreateAsync(new() { Code = "HTTP", Name = "HTTP" }, default);
        var item = await f.Items(setup).CreateAsync(new() { Code = "HTTP", Name = "HTTP", CategoryId = category.Id,
            Unit = "kg", ItemType = ItemType.RawMaterial }, default);
        await using var factory = new RuntimeFactory(f);
        factory.UseKestrel(0);
        using var client = factory.CreateClient();
        Assert.True(client.BaseAddress!.IsLoopback); Assert.NotEqual(0, client.BaseAddress.Port);
        const string route = "/api/v1/purchase-receipts";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.ActorToken("Viewer"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.ActorToken("WarehouseStaff"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(route, new { supplierId = supplier.Id })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.ActorToken("PurchasingStaff"));
        var response = await client.PostAsJsonAsync(route, new { supplierId = supplier.Id,
            lines = new[] { new { itemId = item.Id, quantity = 2.125m, unitPrice = 3.00m } } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var draft = (await response.Content.ReadFromJsonAsync<PurchaseReceiptResponse>())!;
        Assert.Equal(new Uri(client.BaseAddress, $"{route}/{draft.Id}"), new Uri(client.BaseAddress, response.Headers.Location!));
        Assert.Equal(6.38m, draft.TotalAmount); Assert.Equal(f.ActorId, draft.CreatedByUserId);
        await CheckState(draft);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{route}/{draft.Id}")).StatusCode);
        var page = await client.GetFromJsonAsync<PurchaseReceiptPage>($"{route}?supplierId={supplier.Id}&status=0&pageSize=1");
        Assert.Equal(draft.Id, Assert.Single(page!.Items).Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(route, new { supplierId = supplier.Id,
            lines = new[] { new { itemId = item.Id, quantity = 0, unitPrice = 1 } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(route, new { supplierId = supplier.Id, totalAmount = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{route}/{int.MaxValue}")).StatusCode);
        var edit = await client.PutAsJsonAsync($"{route}/{draft.Id}", new { supplierId = supplier.Id, rowVersion = draft.RowVersion,
            lines = new[] { new { itemId = item.Id, quantity = 4, unitPrice = 2 } }, note = "Edited" });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var updated = (await edit.Content.ReadFromJsonAsync<PurchaseReceiptResponse>())!;
        Assert.Equal(8m, updated.TotalAmount); Assert.NotEqual(draft.RowVersion, updated.RowVersion);
        await CheckState(updated);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"{route}/{draft.Id}",
            new { supplierId = supplier.Id, rowVersion = draft.RowVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"{route}/{draft.Id}/cancel", new { rowVersion = draft.RowVersion })).StatusCode);
        var cancel = await client.PostAsJsonAsync($"{route}/{draft.Id}/cancel", new { rowVersion = updated.RowVersion });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var cancelled = (await cancel.Content.ReadFromJsonAsync<PurchaseReceiptResponse>())!;
        Assert.Equal(DocumentStatus.Cancelled, cancelled.Status);
        Assert.Equal(updated.Lines.ToArray(), cancelled.Lines.ToArray());
        await CheckState(cancelled);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"{route}/{draft.Id}/cancel", new { rowVersion = cancelled.RowVersion })).StatusCode);

        async Task CheckState(PurchaseReceiptResponse expected)
        {
            // Context mới sau mỗi HTTP request: không dùng tracked entity làm bằng chứng SQL.
            await using var verify = f.Context();
            var row = await verify.PurchaseReceipts.Include(x => x.Lines).SingleAsync();
            Assert.Equal(expected.Id, row.Id); Assert.Equal(expected.Status, row.Status);
            Assert.Equal(expected.TotalAmount, row.TotalAmount); Assert.Equal(expected.RowVersion, Convert.ToBase64String(row.RowVersion));
            Assert.Equal(expected.Lines.Select(x => (x.Id, x.ItemId, x.Quantity, x.UnitPrice, x.LineTotal)),
                row.Lines.OrderBy(x => x.LineNumber).Select(x => (x.Id, x.ItemId, x.Quantity, x.UnitPrice, x.LineTotal)));
            Assert.Equal(0m, (await verify.InventoryBalances.SingleAsync()).QuantityOnHand);
            Assert.Empty(await verify.InventoryTransactions.ToArrayAsync());
        }
    }

    private sealed class RuntimeFactory(MasterDataSqlFixture fixture) : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder); builder.UseEnvironment("Testing");
            builder.ConfigureServices(services => services.AddScoped<ApplicationDbContext>(_ => fixture.Context()));
        }
        public string ActorToken(string role) => new TokenService(Options.Create(Jwt), TimeProvider.System)
            .Create(new ApplicationUser { Id = fixture.ActorId, UserName = fixture.ActorId }, [role]).AccessToken;
    }
}
