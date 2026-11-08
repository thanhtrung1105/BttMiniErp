using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.DTOs.Categories;
using BttMiniERP.Api.DTOs.Items;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Services;
using BttMiniERP.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace BttMiniERP.Tests.Integration.MasterData;

[Collection("MasterData SQL"), Trait("Category", "SqlServer"), Trait("SqlSuite", "MasterDataRuntime")]
public sealed class MasterDataRuntimeSqlTests(ITestOutputHelper output)
{
    [MasterDataSqlFact]
    public async Task KestrelUsesRealServicesAndSqlForCrudVersionValidationAndAuthorization()
    {
        await using var fixture = await MasterDataSqlFixture.OpenAsync();
        await using var factory = new RuntimeFactory(fixture);
        factory.UseKestrel(0); // TCP loopback thật, không TestServer hoặc service double.
        using var client = factory.CreateClient();
        Assert.True(client.BaseAddress!.IsLoopback);
        Assert.NotEqual(0, client.BaseAddress.Port);
        await Expect(await client.GetAsync("/health"), HttpStatusCode.OK);
        await Problem(await client.GetAsync("/api/v1/items"), HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.ActorToken("Viewer"));
        await Expect(await client.GetAsync("/api/v1/categories"), HttpStatusCode.OK);
        await Problem(await client.PostAsJsonAsync("/api/v1/categories", new { code = "denied", name = "Denied" }), HttpStatusCode.Forbidden);
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.ActorToken("Admin"));
        var created = await Expect(await client.PostAsJsonAsync("/api/v1/categories", new { code = " runtime ", name = "Runtime category" }), HttpStatusCode.Created);
        var category = (await created.Content.ReadFromJsonAsync<CategoryResponse>())!;
        Assert.EndsWith($"/api/v1/categories/{category.Id}", created.Headers.Location!.ToString());
        Assert.Equal("RUNTIME", category.Code);
        await AssertResponseFields(created, "id", "code", "name", "description", "isActive", "createdAt", "createdByUserId", "updatedAt", "updatedByUserId", "rowVersion");
        Assert.Equal(fixture.ActorId, category.CreatedByUserId);
        await Expect(await client.GetAsync($"/api/v1/categories/{category.Id}"), HttpStatusCode.OK);
        await Problem(await client.PostAsJsonAsync("/api/v1/categories", new { code = "runtime", name = "Duplicate" }), HttpStatusCode.Conflict);
        await Problem(await client.PostAsJsonAsync("/api/v1/categories", new { code = "bad", name = "Overpost", createdByUserId = "spoof" }), HttpStatusCode.BadRequest);
        await Problem(await client.PostAsJsonAsync("/api/v1/items", ItemBody(category.Id, "bad", 2)), HttpStatusCode.BadRequest);
        await Problem(await client.PostAsJsonAsync("/api/v1/items", new { code = "bad", name = "Bad", categoryId = category.Id, unit = "kg", itemType = 0, purchasePrice = 1.001m }), HttpStatusCode.BadRequest);

        foreach (var type in new[] { 0, 1 })
        {
            var response = await Expect(await client.PostAsJsonAsync("/api/v1/items", ItemBody(category.Id, "item-" + type, type)), HttpStatusCode.Created);
            var item = (await response.Content.ReadFromJsonAsync<ItemResponse>())!;
            await AssertResponseFields(response, "id", "code", "name", "categoryId", "unit", "itemType", "purchasePrice", "salePrice", "isActive", "createdAt", "createdByUserId", "updatedAt", "updatedByUserId", "rowVersion");
            Assert.Equal(type, (int)item.ItemType);
            var read = await Expect(await client.GetAsync($"/api/v1/items/{item.Id}"), HttpStatusCode.OK);
            Assert.Equal(item, await read.Content.ReadFromJsonAsync<ItemResponse>());
            var list = await Expect(await client.GetAsync($"/api/v1/items?categoryId={category.Id}&itemType={type}&isActive=true"), HttpStatusCode.OK);
            Assert.Equal(item.Id, Assert.Single((await list.Content.ReadFromJsonAsync<ItemPage>())!.Items).Id);
            await Problem(await client.PostAsJsonAsync("/api/v1/items", ItemBody(category.Id, "item-" + type, type)), HttpStatusCode.Conflict);
            client.DefaultRequestHeaders.Authorization = new("Bearer", factory.ActorToken("Manager"));
            var body = new { code = item.Code, name = "Updated", categoryId = category.Id, unit = item.Unit, itemType = type, purchasePrice = 5.50m, salePrice = 10m, rowVersion = item.RowVersion };
            var update = await Expect(await client.PutAsJsonAsync($"/api/v1/items/{item.Id}", body), HttpStatusCode.OK);
            var updated = (await update.Content.ReadFromJsonAsync<ItemResponse>())!;
            Assert.NotEqual(item.RowVersion, updated.RowVersion);
            await Problem(await client.PutAsJsonAsync($"/api/v1/items/{item.Id}", body), HttpStatusCode.Conflict);
            await Problem(await client.DeleteAsync($"/api/v1/items/{item.Id}"), HttpStatusCode.BadRequest);
            await Problem(await Delete(client, "items", item.Id, "bad"), HttpStatusCode.BadRequest);
            await Problem(await Delete(client, "items", item.Id, '"' + item.RowVersion + '"'), HttpStatusCode.Conflict);
            var deleted = await Expect(await Delete(client, "items", item.Id, '"' + updated.RowVersion + '"'), HttpStatusCode.OK);
            Assert.False((await deleted.Content.ReadFromJsonAsync<ItemResponse>())!.IsActive);
            await using var verify = fixture.Context();
            var persisted = await verify.Items.SingleAsync(x => x.Id == item.Id);
            Assert.False(persisted.IsActive);
            Assert.Equal("Updated", persisted.Name);
            Assert.Equal(5.50m, persisted.PurchasePrice);
            Assert.Equal(fixture.ActorId, persisted.UpdatedByUserId);
        }

        var categoryBody = new { code = category.Code, name = "Updated category", rowVersion = category.RowVersion };
        var categoryUpdate = await Expect(await client.PutAsJsonAsync($"/api/v1/categories/{category.Id}", categoryBody), HttpStatusCode.OK);
        var nextCategory = (await categoryUpdate.Content.ReadFromJsonAsync<CategoryResponse>())!;
        Assert.NotEqual(category.RowVersion, nextCategory.RowVersion);
        await Problem(await client.PutAsJsonAsync($"/api/v1/categories/{category.Id}", categoryBody), HttpStatusCode.Conflict);
        await Problem(await client.DeleteAsync($"/api/v1/categories/{category.Id}"), HttpStatusCode.BadRequest);
        await Problem(await Delete(client, "categories", category.Id, "*"), HttpStatusCode.BadRequest);
        await Problem(await Delete(client, "categories", category.Id, '"' + category.RowVersion + '"'), HttpStatusCode.Conflict);
        await Expect(await Delete(client, "categories", category.Id, '"' + nextCategory.RowVersion + '"'), HttpStatusCode.OK);
        await Problem(await client.PostAsJsonAsync("/api/v1/items", ItemBody(category.Id, "inactive-category", 0)), HttpStatusCode.Conflict);
        await Problem(await client.GetAsync("/api/v1/items/2147483647"), HttpStatusCode.NotFound);
        var categories = await Expect(await client.GetAsync("/api/v1/categories?isActive=false"), HttpStatusCode.OK);
        Assert.Equal(category.Id, Assert.Single((await categories.Content.ReadFromJsonAsync<CategoryPage>())!.Items).Id);
        await using var final = fixture.Context();
        Assert.False((await final.Categories.SingleAsync()).IsActive);
        Assert.Equal(2, await final.Items.CountAsync()); // Soft delete giữ cả hai hàng và FK.
        output.WriteLine("Kestrel TCP → production controllers/services → SQL verified; fixture cleans owned rows on disposal.");
    }

    private async Task<HttpResponseMessage> Expect(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        output.WriteLine($"{response.RequestMessage!.Method} {response.RequestMessage.RequestUri!.AbsolutePath}: {(int)response.StatusCode}");
        await Task.CompletedTask;
        return response;
    }
    private async Task Problem(HttpResponseMessage response, HttpStatusCode status)
    {
        await Expect(response, status);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var content = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(content);
        Assert.Equal((int)status, json.RootElement.GetProperty("status").GetInt32());
        foreach (var forbidden in new[] { "SqlException", "stackTrace", "PasswordHash", "SigningKey", "SELECT ", "INSERT INTO" }) Assert.DoesNotContain(forbidden, content);
    }
    private static async Task AssertResponseFields(HttpResponseMessage response, params string[] fields)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(fields.Order(), json.RootElement.EnumerateObject().Select(x => x.Name).Order());
    }
    private static object ItemBody(int categoryId, string code, int type)
        => new { code, name = "Runtime item", categoryId, unit = "kg", itemType = type, purchasePrice = 12.34m, salePrice = 15m };
    private static Task<HttpResponseMessage> Delete(HttpClient client, string resource, int id, string version)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/{resource}/{id}");
        request.Headers.TryAddWithoutValidation("If-Match", version);
        return client.SendAsync(request);
    }

    private sealed class RuntimeFactory(MasterDataSqlFixture fixture) : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:DefaultConnection"] = fixture.Connection }));
            // Fixture đã kiểm chứng target/schema trước khi tạo host; chỉ thay đăng ký DbContext
            // để dùng guard/ownership profile test. Giữ nguyên mọi service, JWT và policy production.
            builder.ConfigureServices(services => services.AddScoped<ApplicationDbContext>(_ => fixture.Context()));
        }
        public string ActorToken(params string[] roles) => new TokenService(Options.Create(Jwt), TimeProvider.System)
            .Create(new ApplicationUser { Id = fixture.ActorId, UserName = fixture.ActorId }, roles).AccessToken;
    }
}
