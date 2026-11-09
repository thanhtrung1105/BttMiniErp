using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Services;
using BttMiniERP.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace BttMiniERP.Tests.Integration.Partners;

[Collection("Partners SQL"), Trait("Category", "SqlServer"), Trait("SqlSuite", "PartnersRuntime")]
public sealed class PartnersRuntimeSqlTests(ITestOutputHelper output)
{
    [PartnersSqlFact]
    public async Task KestrelPersistsPartnersAndEnforcesAllRolesAndContracts()
    {
        await using var fixture = await PartnersSqlFixture.OpenAsync();
        await using var factory = new RuntimeFactory(fixture);
        factory.UseKestrel(0);
        using var client = factory.CreateClient();
        Assert.True(client.BaseAddress!.IsLoopback);
        Assert.NotEqual(0, client.BaseAddress.Port);
        await Expect(await client.GetAsync("/health"), HttpStatusCode.OK);
        foreach (var resource in new[] { "suppliers", "customers" })
        {
            var path = "/api/v1/" + resource;
            var owner = resource == "suppliers" ? "PurchasingStaff" : "SalesStaff";
            client.DefaultRequestHeaders.Authorization = null;
            await Problem(await client.GetAsync(path), HttpStatusCode.Unauthorized);
            foreach (var role in new[] { "Admin", "Manager", "PurchasingStaff", "SalesStaff", "ProductionStaff", "WarehouseStaff", "Viewer" })
            {
                client.DefaultRequestHeaders.Authorization = new("Bearer", factory.ActorToken(role));
                bool write = role is "Admin" or "Manager" || role == owner;
                bool read = write || role is "WarehouseStaff" or "Viewer";
                if (read) await Expect(await client.GetAsync(path), HttpStatusCode.OK);
                else await Problem(await client.GetAsync(path), HttpStatusCode.Forbidden);
                if (!write)
                {
                    await Problem(await client.PostAsJsonAsync(path, new { code = "denied", name = "Denied" }), HttpStatusCode.Forbidden);
                    await Problem(await client.PutAsJsonAsync(path + "/1", new { code = "denied", name = "Denied", rowVersion = "AAAAAAAAAAA=" }), HttpStatusCode.Forbidden);
                    await Problem(await Delete(client, resource, 1, "\"AAAAAAAAAAA=\""), HttpStatusCode.Forbidden);
                }
            }
            client.DefaultRequestHeaders.Authorization = new("Bearer", factory.ActorToken(owner));
            var created = await Expect(await client.PostAsJsonAsync(path, new { code = " runtime ", name = "Partner", email = "a@example.com", phone = "0123456789", address = "Address" }), HttpStatusCode.Created);
            using var doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            var row = doc.RootElement;
            var id = row.GetProperty("id").GetInt32();
            var version = row.GetProperty("rowVersion").GetString()!;
            Assert.Equal("RUNTIME", row.GetProperty("code").GetString());
            Assert.Equal(fixture.ActorId, row.GetProperty("createdByUserId").GetString());
            Assert.EndsWith(path + "/" + id, created.Headers.Location!.ToString());
            var fields = new List<string> { "id", "code", "name", "phone", "email", "address", "isActive", "createdAt", "createdByUserId", "updatedAt", "updatedByUserId", "rowVersion" };
            if (resource == "suppliers") fields.Add("taxCode");
            await AssertResponseFields(created, fields.ToArray());
            await Expect(await client.GetAsync(path + "/" + id), HttpStatusCode.OK);
            await Problem(await client.GetAsync(path + "/2147483647"), HttpStatusCode.NotFound);
            await Problem(await client.PostAsJsonAsync(path, new { code = "runtime", name = "Duplicate" }), HttpStatusCode.Conflict);
            await Problem(await client.PostAsJsonAsync(path, new { code = "bad", name = "Bad", createdByUserId = "spoof" }), HttpStatusCode.BadRequest);
            await Problem(await client.PostAsJsonAsync(path, new { code = "bad", name = "Bad", email = "invalid" }), HttpStatusCode.BadRequest);
            await Problem(await client.PostAsJsonAsync(path, new { code = " ", name = "Bad" }), HttpStatusCode.BadRequest);
            client.DefaultRequestHeaders.Authorization = new("Bearer", factory.ActorToken("Admin"));
            await Expect(await client.PostAsJsonAsync(path, new { code = "second", name = "Second" }), HttpStatusCode.Created);
            var page = await Expect(await client.GetAsync(path + "?isActive=true&page=2&pageSize=1&sortBy=code&sortDirection=asc"), HttpStatusCode.OK);
            using var pageDoc = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
            Assert.Equal(2, pageDoc.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal("SECOND", Assert.Single(pageDoc.RootElement.GetProperty("items").EnumerateArray()).GetProperty("code").GetString());
            client.DefaultRequestHeaders.Authorization = new("Bearer", factory.ActorToken("Manager"));
            var updateBody = new { code = "runtime", name = "Updated", rowVersion = version };
            var update = await Expect(await client.PutAsJsonAsync(path + "/" + id, updateBody), HttpStatusCode.OK);
            using var updated = JsonDocument.Parse(await update.Content.ReadAsStringAsync());
            var nextVersion = updated.RootElement.GetProperty("rowVersion").GetString()!;
            Assert.NotEqual(version, nextVersion);
            await Problem(await client.PutAsJsonAsync(path + "/" + id, updateBody), HttpStatusCode.Conflict);
            await Problem(await client.DeleteAsync(path + "/" + id), HttpStatusCode.BadRequest);
            await Problem(await Delete(client, resource, id, "*"), HttpStatusCode.BadRequest);
            await Problem(await Delete(client, resource, id, '"' + version + '"'), HttpStatusCode.Conflict);
            await Expect(await Delete(client, resource, id, '"' + nextVersion + '"'), HttpStatusCode.OK);
            var search = await Expect(await client.GetAsync(path + "?search=runtime&isActive=false"), HttpStatusCode.OK);
            using var searchDoc = JsonDocument.Parse(await search.Content.ReadAsStringAsync());
            Assert.Equal(id, Assert.Single(searchDoc.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetInt32());
            await using var db = fixture.Context();
            if (resource == "suppliers") { Assert.Equal(2, await db.Suppliers.CountAsync()); Assert.False((await db.Suppliers.SingleAsync(x => x.Id == id)).IsActive); }
            else { Assert.Equal(2, await db.Customers.CountAsync()); Assert.False((await db.Customers.SingleAsync(x => x.Id == id)).IsActive); }
        }
    }
    [PartnersSqlFact]
    public async Task ConcurrentHttpCreatesReturn201And409FromUniqueConstraint()
    {
        await using var fixture = await PartnersSqlFixture.OpenAsync();
        foreach (var resource in new[] { "suppliers", "customers" })
        {
            var barrier = new TwoPartnerSaves();
            await using var factory = new RuntimeFactory(fixture, barrier);
            factory.UseKestrel(0);
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", factory.ActorToken("Admin"));
            var results = await Task.WhenAll(
                client.PostAsJsonAsync("/api/v1/" + resource, new { code = "race", name = "First" }),
                client.PostAsJsonAsync("/api/v1/" + resource, new { code = " RACE ", name = "Second" }));
            Assert.Equal(new[] { 201, 409 }, results.Select(x => (int)x.StatusCode).Order());
            await Problem(results.Single(x => x.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict);
            await using var verify = fixture.Context();
            if (resource == "suppliers") Assert.Equal(1, await verify.Suppliers.CountAsync());
            else Assert.Equal(1, await verify.Customers.CountAsync());
            foreach (var response in results) response.Dispose();
        }
    }

    private sealed class TwoPartnerSaves : SaveChangesInterceptor
    {
        private int arrived;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref arrived) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            return result;
        }
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
    private static Task<HttpResponseMessage> Delete(HttpClient client, string resource, int id, string version)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/{resource}/{id}");
        request.Headers.TryAddWithoutValidation("If-Match", version);
        return client.SendAsync(request);
    }

    private sealed class RuntimeFactory(PartnersSqlFixture fixture, params IInterceptor[] interceptors) : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:DefaultConnection"] = fixture.Connection }));
            // Fixture đã kiểm chứng target/schema trước khi tạo host; chỉ thay đăng ký DbContext
            // để dùng guard/ownership profile test. Giữ nguyên mọi service, JWT và policy production.
            builder.ConfigureServices(services => services.AddScoped<ApplicationDbContext>(_ => fixture.Context(interceptors)));
        }
        public string ActorToken(params string[] roles) => new TokenService(Options.Create(Jwt), TimeProvider.System)
            .Create(new ApplicationUser { Id = fixture.ActorId, UserName = fixture.ActorId }, roles).AccessToken;
    }
}
