using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BttMiniERP.Tests.Fixtures;

namespace BttMiniERP.Tests.Integration.Partners;

public sealed class PartnersHttpTests
{
    [Theory]
    [InlineData("Admin")][InlineData("Manager")][InlineData("PurchasingStaff")][InlineData("SalesStaff")]
    [InlineData("WarehouseStaff")][InlineData("Viewer")][InlineData("ProductionStaff")][InlineData("")][InlineData(null)]
    public async Task EveryEndpointEnforcesExactRoleMatrix(string? role)
    {
        await using var factory = new PartnersApiFactory();
        using var client = factory.CreateClient();
        if (role != null) client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token(role == "" ? [] : [role]));
        var calls = 0;
        foreach (var resource in new[] { "suppliers", "customers" })
        foreach (var method in new[] { "GET", "DETAIL", "POST", "PUT", "DELETE" })
        {
            var owner = resource == "suppliers" ? "PurchasingStaff" : "SalesStaff";
            var canWrite = role is "Admin" or "Manager" || role == owner;
            var canRead = canWrite || role is "WarehouseStaff" or "Viewer";
            var read = method is "GET" or "DETAIL";
            using var request = new HttpRequestMessage(new HttpMethod(method == "DETAIL" ? "GET" : method),
                $"/api/v1/{resource}" + (method is "DETAIL" or "PUT" or "DELETE" ? "/1" : ""));
            if (method is "POST" or "PUT") request.Content = JsonContent.Create(Body(method == "PUT"));
            if (method == "DELETE") request.Headers.TryAddWithoutValidation("If-Match", "\"AAAAAAAAAAA=\"");
            using var response = await client.SendAsync(request);
            var expected = role == null ? HttpStatusCode.Unauthorized : !(read ? canRead : canWrite)
                ? HttpStatusCode.Forbidden : method == "POST" ? HttpStatusCode.Created : HttpStatusCode.OK;
            Assert.Equal(expected, response.StatusCode);
            if (expected is HttpStatusCode.OK or HttpStatusCode.Created) calls++;
            if (expected == HttpStatusCode.Created) Assert.Equal($"/api/v1/{resource}/1", response.Headers.Location!.AbsolutePath);
            if (expected == HttpStatusCode.OK && method == "DELETE")
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.False(json.RootElement.GetProperty("isActive").GetBoolean());
            }
        }
        Assert.Equal(calls, factory.Calls);
    }

    [Theory]
    [InlineData("{}")][InlineData("{\"code\":\" \",\"name\":\"a\"}")]
    [InlineData("{\"code\":\"a\",\"name\":\"a\",\"email\":\"invalid\"}")]
    [InlineData("{\"code\":\"a\",\"name\":\"a\",\"createdByUserId\":\"attacker\"}")]
    [InlineData("{\"code\":\"a\",\"name\":\"a\",\"createdAt\":\"2026-01-01\"}")]
    public async Task InvalidOrServerOwnedFieldsAreRejectedBeforeService(string body)
    {
        await using var factory = new PartnersApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token("Admin"));
        foreach (var resource in new[] { "suppliers", "customers" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/v1/{resource}", new StringContent(body, Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(0, factory.Calls);
    }

    [Theory]
    [InlineData("suppliers")][InlineData("customers")]
    public async Task QueryBindingVersionsAndOpenApiFollowMasterDataContract(string resource)
    {
        await using var factory = new PartnersApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token("Admin"));
        foreach (var query in new[] { "page=0", "pageSize=101", "sortBy=password", "sortDirection=wrong" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/{resource}?{query}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"/api/v1/{resource}/1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/v1/{resource}/1", Body(false))).StatusCode);
        Assert.Equal(0, factory.Calls);
        using var result = await client.GetAsync($"/api/v1/{resource}?search=partner&isActive=false&page=2&pageSize=3&sortBy=name&sortDirection=desc");
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        using var json = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
        Assert.Equal(2, json.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(3, json.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal("partner", factory.LastQuery!.Search);
        Assert.False(factory.LastQuery.IsActive);
        Assert.Equal("name", factory.LastQuery.SortBy);
        Assert.Equal("desc", factory.LastQuery.SortDirection);
        using var openApi = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var detail = openApi.RootElement.GetProperty("paths").GetProperty($"/api/v1/{resource}/{{id}}");
        foreach (var method in new[] { "get", "put", "delete" }) Assert.True(detail.TryGetProperty(method, out _));
    }

    [Theory]
    [InlineData(404)][InlineData(409)]
    public async Task ServiceErrorsUseSafeProblemDetails(int status)
    {
        await using var factory = new PartnersApiFactory { ErrorStatus = status };
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token("Admin"));
        foreach (var resource in new[] { "suppliers", "customers" })
        {
            var response = await client.GetAsync($"/api/v1/{resource}/1");
            Assert.Equal(status, (int)response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        }
    }

    private static Dictionary<string, object> Body(bool update)
    {
        var body = new Dictionary<string, object> { ["code"] = "PARTNER", ["name"] = "Partner", ["email"] = "a@example.com" };
        if (update) body["rowVersion"] = "AAAAAAAAAAA=";
        return body;
    }
}
