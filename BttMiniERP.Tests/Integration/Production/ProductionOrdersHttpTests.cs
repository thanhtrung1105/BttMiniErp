using System.Net;
using System.Net.Http.Json;
using System.Text;
using BttMiniERP.Api.DTOs.ProductionOrders;
using BttMiniERP.Api.Enums;
using BttMiniERP.Tests.Fixtures;

namespace BttMiniERP.Tests.Integration.Production;

public sealed class ProductionOrdersHttpTests
{
    private const string Route = "/api/v1/production-orders";
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("Admin")] [InlineData("Manager")]
    [InlineData("ProductionStaff")] [InlineData("WarehouseStaff")] [InlineData("Viewer")]
    [InlineData("PurchasingStaff")] [InlineData("SalesStaff")]
    public async Task FiveEndpointsEnforceExactPermissions(string? role)
    {
        await using var f = new ProductionOrdersApiFactory(); using var client = f.CreateClient();
        if (role != null) client.DefaultRequestHeaders.Authorization = new("Bearer", f.Token(role=="" ? [] : [role]));
        var calls=0;
        foreach (var op in new[] { "list", "get", "create", "update", "cancel" })
        {
            var read=op is "list" or "get";
            var allowed=role is "Admin" or "Manager" or "ProductionStaff" || (read && role=="WarehouseStaff");
            using var request = new HttpRequestMessage(read ? HttpMethod.Get : op=="update" ? HttpMethod.Put : HttpMethod.Post,
                Route+(op=="cancel" ? "/1/cancel" : op is "get" or "update" ? "/1" : ""));
            if(op=="create") request.Content=JsonContent.Create(new { bomId=1, quantity=3m });
            if(op=="update") request.Content=JsonContent.Create(new { quantity=4m, rowVersion="AAAAAAAAAAA=" });
            if(op=="cancel") request.Content=JsonContent.Create(new { rowVersion="AAAAAAAAAAA=" });
            var response=await client.SendAsync(request);
            Assert.Equal(role==null ? 401 : !allowed ? 403 : op=="create" ? 201 : 200,(int)response.StatusCode);
            if (!allowed) continue;
            calls++;
            if(op=="create") Assert.EndsWith(Route+"/1",response.Headers.Location!.ToString());
            if(op!="list") {
                var dto=(await response.Content.ReadFromJsonAsync<ProductionOrderResponse>())!;
                Assert.Equal(op=="cancel" ? DocumentStatus.Cancelled : DocumentStatus.Draft,dto.Status);
                Assert.Equal(op=="update" ? 8 : 6,Assert.Single(dto.Materials).RequiredQuantity);
                Assert.Null(dto.Materials[0].ActualQuantity); Assert.Null(dto.ConfirmedAt);
                Assert.Equal(2,dto.BomVersionSnapshot);
            }
        }
        Assert.Equal(calls,f.Calls);
    }

    [Theory]
    [InlineData("status","1")] [InlineData("orderNumber","\"PO-SPOOF\"")]
    [InlineData("createdByUserId","\"spoof\"")] [InlineData("finishedProductId","10")]
    [InlineData("bomVersionSnapshot","9")] [InlineData("materials","[]")]
    [InlineData("actualQuantity","2")]
    public async Task CreateRejectsServerManagedFields(string field,string value)
    {
        await using var f=new ProductionOrdersApiFactory(); using var c=f.CreateClient();
        c.DefaultRequestHeaders.Authorization=new("Bearer",f.Token("ProductionStaff"));
        var body="{\"bomId\":1,\"quantity\":3,\""+field+"\":"+value+"}";
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsync(Route,new StringContent(body,Encoding.UTF8,"application/json"))).StatusCode);
        Assert.Equal(0,f.Calls);
    }

    [Fact]
    public async Task InvalidInputsImmutableBomAndMissingVersionAreRejected()
    {
        await using var f=new ProductionOrdersApiFactory(); using var c=f.CreateClient();
        c.DefaultRequestHeaders.Authorization=new("Bearer",f.Token("Admin"));
        foreach(var q in new[] { "page=0","pageSize=101","bomId=0","finishedProductId=0","status=99","sortBy=quantity","sortDirection=bad","from=2026-02-01&to=2026-01-01" })
            Assert.Equal(HttpStatusCode.BadRequest,(await c.GetAsync(Route+"?"+q)).StatusCode);
        foreach(var quantity in new[] {0m,-1m,0.0001m,1000000000000000m})
            Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync(Route,new {bomId=1,quantity})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync(Route,new {bomId=0,quantity=1})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PutAsJsonAsync(Route+"/1",new {quantity=1})).StatusCode);
        foreach(var version in new string?[]{null,"bad","*"})
            Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync(Route+"/1/cancel",new {rowVersion=version})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PutAsJsonAsync(Route+"/1",new {quantity=1,bomId=2,rowVersion="AAAAAAAAAAA="})).StatusCode);
        Assert.Equal(0,f.Calls);
        Assert.Equal(HttpStatusCode.OK,(await c.GetAsync(Route+"?bomId=2&finishedProductId=10&status=2&page=2&pageSize=5&sortBy=id&sortDirection=asc")).StatusCode);
        Assert.Equal(2,f.LastQuery!.BomId); Assert.Equal(DocumentStatus.Cancelled,f.LastQuery.Status);
        Assert.Equal(2,f.LastQuery.Page);
        Assert.Equal(HttpStatusCode.NotFound,(await c.PostAsJsonAsync(Route+"/1/confirm",new {rowVersion="AAAAAAAAAAA="})).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,(await c.DeleteAsync(Route+"/1")).StatusCode);
    }

    [Theory] [InlineData(400)] [InlineData(404)] [InlineData(409)] [InlineData(500)]
    public async Task ServiceErrorsUseProblemDetails(int status)
    {
        await using var f=new ProductionOrdersApiFactory { ErrorStatus=status }; using var c=f.CreateClient();
        c.DefaultRequestHeaders.Authorization=new("Bearer",f.Token("Admin"));
        var response=await c.GetAsync(Route+"/1");
        Assert.Equal(status,(int)response.StatusCode);
        Assert.Equal("application/problem+json",response.Content.Headers.ContentType!.MediaType);
        var body=await response.Content.ReadAsStringAsync(); Assert.Contains("traceId",body); Assert.DoesNotContain("StackTrace",body);
    }
}
