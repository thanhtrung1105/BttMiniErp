using System.Net;
using System.Net.Http.Json;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.DTOs.Items;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Services;
using BttMiniERP.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BttMiniERP.Tests.Integration.Inventory;

[Collection("MasterData SQL"), Trait("Category", "SqlServer"), Trait("SqlSuite", "InventoryRuntime")]
public sealed class InventoryRuntimeSqlTests
{
    [InventorySqlFact]
    public async Task KestrelItemCreationCreatesZeroBalanceAndRejectsQuantityOrAuditOverposting()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Inventory");
        await using var setup = f.Context();
        var category = await f.Categories(setup).CreateAsync(new() { Code = "HTTP", Name = "HTTP" }, default);
        await using var factory = new RuntimeFactory(f);
        factory.UseKestrel(0);
        using var client = factory.CreateClient();
        Assert.True(client.BaseAddress!.IsLoopback); Assert.NotEqual(0, client.BaseAddress.Port);
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.ActorToken());
        var response = await client.PostAsJsonAsync("/api/v1/items", new { code = "HTTP", name = "HTTP", categoryId = category.Id, unit = "kg", itemType = 0 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var item = (await response.Content.ReadFromJsonAsync<ItemResponse>())!;
        Assert.Equal(f.ActorId, item.CreatedByUserId);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/items", new {
            code = "QUANTITY", name = "Invalid", categoryId = category.Id, unit = "kg", itemType = 0, quantityOnHand = 100 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/items", new {
            code = "AUDIT", name = "Invalid", categoryId = category.Id, unit = "kg", itemType = 0, createdByUserId = "spoof" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/items/{item.Id}")).StatusCode);
        await using var verify = f.Context();
        Assert.Equal(item.Id, (await verify.Items.SingleAsync()).Id);
        var balance = await verify.InventoryBalances.SingleAsync();
        Assert.Equal(item.Id, balance.ItemId); Assert.Equal(0m, balance.QuantityOnHand);
        Assert.Equal(item.CreatedAt, balance.CreatedAt); Assert.Equal(f.ActorId, balance.CreatedByUserId);
        Assert.Empty(await verify.InventoryTransactions.ToArrayAsync());
    }

    private sealed class RuntimeFactory(MasterDataSqlFixture fixture) : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder); builder.UseEnvironment("Testing");
            // Guard/ownership đã xác minh trước host; giữ controllers/services/policies production.
            builder.ConfigureServices(services => services.AddScoped<ApplicationDbContext>(_ => fixture.Context()));
        }
        public string ActorToken() => new TokenService(Options.Create(Jwt), TimeProvider.System)
            .Create(new ApplicationUser { Id = fixture.ActorId, UserName = fixture.ActorId }, ["Admin"]).AccessToken;
    }
}
