using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BttMiniERP.Tests.Fixtures;

namespace BttMiniERP.Tests.Integration.MasterData;

public sealed class MasterDataHttpTests
{
    [Theory]
    [InlineData("Admin", true)][InlineData("Manager", true)][InlineData("PurchasingStaff", false)]
    [InlineData("SalesStaff", false)][InlineData("ProductionStaff", false)][InlineData("WarehouseStaff", false)]
    [InlineData("Viewer", false)][InlineData("", false)][InlineData(null, false)]
    public async Task EveryRouteEnforcesReadAndWriteMatrix(string? role, bool canWrite)
    {
        await using var factory = new MasterDataApiFactory();
        using var client = factory.CreateClient();
        if (role != null) client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token(role == "" ? [] : [role]));
        foreach (var resource in new[] { "categories", "items" })
        foreach (var method in new[] { "GET", "DETAIL", "POST", "PUT", "DELETE" })
        {
            var read = method is "GET" or "DETAIL";
            using var request = new HttpRequestMessage(new HttpMethod(method == "DETAIL" ? "GET" : method),
                $"/api/v1/{resource}" + (method is "DETAIL" or "PUT" or "DELETE" ? "/1" : ""));
            if (method is "POST" or "PUT") request.Content = JsonContent.Create(Body(resource, method == "PUT"));
            if (method == "DELETE") request.Headers.TryAddWithoutValidation("If-Match", "\"AAAAAAAAAAA=\"");
            using var response = await client.SendAsync(request);
            var expected = role == null ? HttpStatusCode.Unauthorized : role == "" || (!read && !canWrite)
                ? HttpStatusCode.Forbidden : method == "POST" ? HttpStatusCode.Created : HttpStatusCode.OK;
            Assert.Equal(expected, response.StatusCode);
            if (expected == HttpStatusCode.Created) Assert.Equal($"/api/v1/{resource}/1", response.Headers.Location!.AbsolutePath);
            if (expected is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        }
        Assert.Equal(role == null || role == "" ? 0 : canWrite ? 10 : 4, factory.Calls);
    }

    [Theory]
    [InlineData("categories", "{}")]
    [InlineData("categories", "{\"code\":\"  \",\"name\":\"a\"}")]
    [InlineData("categories", "{\"code\":\"a\",\"name\":\"b\",\"createdByUserId\":\"other\"}")]
    [InlineData("items", "{\"code\":\"a\",\"name\":\"b\",\"categoryId\":1,\"unit\":\"kg\",\"itemType\":2}")]
    [InlineData("items", "{\"code\":\"a\",\"name\":\"b\",\"categoryId\":1,\"unit\":\"kg\"}")]
    [InlineData("items", "{\"code\":\"a\",\"name\":\"b\",\"categoryId\":1,\"unit\":\"kg\",\"itemType\":0,\"salePrice\":0.001}")]
    [InlineData("items", "{\"code\":\"a\",\"name\":\"b\",\"categoryId\":1,\"unit\":\"kg\",\"itemType\":0,\"balance\":10}")]
    public async Task InvalidAndProtectedFieldsAreRejectedBeforeService(string resource, string json)
    {
        await using var factory = new MasterDataApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token("Admin"));
        var response = await client.PostAsync($"/api/v1/{resource}", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.Calls);
    }

    [Theory]
    [InlineData("categories?pageSize=101")][InlineData("categories?sortBy=password")]
    [InlineData("items?categoryId=0")][InlineData("items?itemType=9")][InlineData("items?page=0")]
    public async Task InvalidQueryIsRejected(string path)
    {
        await using var factory = new MasterDataApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token("Viewer"));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/" + path)).StatusCode);
        Assert.Equal(0, factory.Calls);
    }

    [Theory]
    [InlineData("categories")][InlineData("items")]
    public async Task WritesRequireVersionAndOpenApiDescribesAllRoutes(string resource)
    {
        await using var factory = new MasterDataApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token("Admin"));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"/api/v1/{resource}/1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/v1/{resource}/1", Body(resource, false))).StatusCode);
        Assert.Equal(0, factory.Calls);
        using var json = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = json.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty($"/api/v1/{resource}").TryGetProperty("post", out _));
        var detail = paths.GetProperty($"/api/v1/{resource}/{{id}}");
        foreach (var method in new[] { "get", "put", "delete" }) Assert.True(detail.TryGetProperty(method, out _));
        Assert.Contains(detail.GetProperty("delete").GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("name").GetString() == "If-Match" && parameter.GetProperty("in").GetString() == "header");
    }

    private static Dictionary<string, object> Body(string resource, bool update)
    {
        var result = new Dictionary<string, object> { ["code"] = "CODE", ["name"] = "Name" };
        if (resource == "items") { result["categoryId"] = 1; result["unit"] = "kg"; result["itemType"] = 0; }
        if (update) result["rowVersion"] = "AAAAAAAAAAA=";
        return result;
    }
}
