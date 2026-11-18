using System.Net;
using System.Net.Http.Json;
using System.Text;
using BttMiniERP.Api.DTOs.BillOfMaterials;
using BttMiniERP.Tests.Fixtures;

namespace BttMiniERP.Tests.Integration.Bom;

public sealed class BillOfMaterialsHttpTests
{
    private const string Route = "/api/v1/bill-of-materials";
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("Admin")] [InlineData("Manager")]
    [InlineData("ProductionStaff")] [InlineData("WarehouseStaff")] [InlineData("Viewer")]
    [InlineData("PurchasingStaff")] [InlineData("SalesStaff")]
    public async Task AllFiveEndpointsEnforceExactRoleMatrix(string? role)
    {
        await using var f = new BillOfMaterialsApiFactory(); using var client = f.CreateClient();
        if (role != null) client.DefaultRequestHeaders.Authorization = new("Bearer", f.Token(role == "" ? [] : [role]));
        var calls = 0;
        foreach (var op in new[] { "list", "get", "create", "update", "delete" })
        {
            var read = op is "list" or "get";
            var allowed = role is "Admin" or "Manager" || (read && role is "ProductionStaff" or "WarehouseStaff" or "Viewer");
            using var request = new HttpRequestMessage(read ? HttpMethod.Get : op == "create" ? HttpMethod.Post : op == "update" ? HttpMethod.Put : HttpMethod.Delete,
                Route + (op is "get" or "update" or "delete" ? "/1" : ""));
            if (op == "delete") request.Headers.TryAddWithoutValidation("If-Match", "\"AAAAAAAAAAA=\"");
            if (op == "create") request.Content = JsonContent.Create(new { code = "B", finishedProductId = 1, details = new[] { new { rawMaterialId = 2, quantityPerUnit = 1.125m } } });
            if (op == "update") request.Content = JsonContent.Create(new { code = "B", finishedProductId = 1, rowVersion = "AAAAAAAAAAA=", details = new[] { new { rawMaterialId = 2, quantityPerUnit = 2m } } });
            var response = await client.SendAsync(request);
            Assert.Equal(role == null ? 401 : !allowed ? 403 : op == "create" ? 201 : 200, (int)response.StatusCode);
            if (allowed)
            {
                calls++;
                if (op == "create") Assert.EndsWith(Route + "/1", response.Headers.Location!.ToString());
                if (!read || op == "get")
                {
                    var dto = (await response.Content.ReadFromJsonAsync<BillOfMaterialResponse>())!;
                    Assert.Equal(op != "delete", dto.IsActive); Assert.Equal(1.125m, Assert.Single(dto.Details).QuantityPerUnit);
                }
            }
        }
        Assert.Equal(calls, f.Calls);
    }

    [Theory]
    [InlineData("version", "2")] [InlineData("createdByUserId", "\"spoof\"")]
    [InlineData("createdAt", "\"2026-01-01T00:00:00Z\"")] [InlineData("itemType", "1")]
    public async Task ServerManagedFieldsAreRejected(string field, string value)
    {
        await using var f = new BillOfMaterialsApiFactory(); using var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new("Bearer", f.Token("Admin"));
        var json = "{\"code\":\"B\",\"finishedProductId\":1,\"details\":[{\"rawMaterialId\":2,\"quantityPerUnit\":1}],\"" + field + "\":" + value + "}";
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync(Route, new StringContent(json, Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(0, f.Calls);
    }

    [Fact]
    public async Task QueryNestedValidationAndVersionContracts()
    {
        await using var f = new BillOfMaterialsApiFactory(); using var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new("Bearer", f.Token("Admin"));
        foreach (var q in new[] { "page=0", "pageSize=101", "finishedProductId=0", "sortBy=version", "sortDirection=bad" })
            Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(Route + "?" + q)).StatusCode);
        foreach (var json in new[] { "{\"code\":\"B\",\"finishedProductId\":1}",
            "{\"code\":\"B\",\"finishedProductId\":1,\"details\":null}",
            "{\"code\":\"B\",\"finishedProductId\":1,\"details\":[{\"rawMaterialId\":2,\"quantityPerUnit\":1,\"id\":4}]}" })
            Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync(Route, new StringContent(json, Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync(Route + "/1", new { code = "B", finishedProductId = 1, details = new[] { new { rawMaterialId = 2, quantityPerUnit = 1 } } })).StatusCode);
        foreach (var header in new string?[] { null, "*", "AAAAAAAAAAA=", "\"bad\"" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, Route + "/1");
            if (header != null) request.Headers.TryAddWithoutValidation("If-Match", header);
            Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(request)).StatusCode);
        }
        Assert.Equal(0, f.Calls);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(Route + "?search=B&isActive=false&finishedProductId=3&page=2&pageSize=5&sortBy=id&sortDirection=desc")).StatusCode);
        Assert.Equal(3, f.LastQuery!.FinishedProductId); Assert.False(f.LastQuery.IsActive); Assert.Equal(2, f.LastQuery.Page);
    }

    [Theory] [InlineData(400)] [InlineData(404)] [InlineData(409)] [InlineData(500)]
    public async Task ErrorsUseProblemDetails(int status)
    {
        await using var f = new BillOfMaterialsApiFactory { ErrorStatus = status }; using var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new("Bearer", f.Token("Manager"));
        var response = await c.GetAsync(Route + "/1");
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.DoesNotContain("stackTrace", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }
}
