using System.Net;
using System.Net.Http.Json;
using System.Text;
using BttMiniERP.Api.DTOs.PurchaseReceipts;
using BttMiniERP.Api.Enums;
using BttMiniERP.Tests.Fixtures;
namespace BttMiniERP.Tests.Integration.Purchasing;

public sealed class PurchaseReceiptsHttpTests
{
    [Theory]
    [InlineData("Admin")][InlineData("Manager")][InlineData("PurchasingStaff")][InlineData("WarehouseStaff")]
    [InlineData("SalesStaff")][InlineData("ProductionStaff")][InlineData("Viewer")][InlineData(null)][InlineData("")]
    public async Task AllFiveEndpointsEnforceRoles(string? role)
    {
        await using var f = new PurchaseReceiptsApiFactory(); using var client=f.CreateClient();
        if (role!=null) client.DefaultRequestHeaders.Authorization=new("Bearer",f.Token(role=="" ? [] : [role]));
        var allowed=0;
        foreach(var op in new[]{"list","get","create","update","cancel"})
        {
            var read=op is "list" or "get";
            var can=role is "Admin" or "Manager" or "PurchasingStaff" || (read && role=="WarehouseStaff");
            var path="/api/v1/purchase-receipts"+(op is "get" or "update" or "cancel" ? "/1" : "")+(op=="cancel" ? "/cancel" : "");
            using var request=new HttpRequestMessage(read?HttpMethod.Get:op=="update"?HttpMethod.Put:HttpMethod.Post,path);
            if(!read) request.Content=op=="create"?JsonContent.Create(new{supplierId=1}):op=="update"?JsonContent.Create(new{supplierId=1,rowVersion="AAAAAAAAAAA="}):JsonContent.Create(new{rowVersion="AAAAAAAAAAA="});
            using var response=await client.SendAsync(request);
            var expected=role==null?HttpStatusCode.Unauthorized:!can?HttpStatusCode.Forbidden:op=="create"?HttpStatusCode.Created:HttpStatusCode.OK;
            Assert.Equal(expected,response.StatusCode);
            if(can) { allowed++; if(op=="create") Assert.EndsWith("/api/v1/purchase-receipts/1",response.Headers.Location!.ToString());
                if(op=="cancel") Assert.Equal(DocumentStatus.Cancelled,(await response.Content.ReadFromJsonAsync<PurchaseReceiptResponse>())!.Status); }
        }
        Assert.Equal(allowed,f.Calls);
    }
    [Theory]
    [InlineData("receiptNumber","\"CLIENT\"")][InlineData("status","2")][InlineData("totalAmount","1")]
    [InlineData("createdByUserId","\"spoof\"")][InlineData("rowVersion","\"AAAAAAAAAAA=\"")]
    [InlineData("cancelledAt","\"2026-01-01T00:00:00Z\"")][InlineData("quantityOnHand","99")]
    public async Task CreateRejectsServerOwnedFields(string field,string value)
    {
        await using var f=new PurchaseReceiptsApiFactory(); using var c=f.CreateClient(); c.DefaultRequestHeaders.Authorization=new("Bearer",f.Token("Admin"));
        var body="{\"supplierId\":1,\""+field+"\":"+value+"}";
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsync("/api/v1/purchase-receipts",new StringContent(body,Encoding.UTF8,"application/json"))).StatusCode);
        Assert.Equal(0,f.Calls);
    }
    [Fact]
    public async Task QueryValidationNestedOverpostingAndVersions()
    {
        await using var f=new PurchaseReceiptsApiFactory(); using var c=f.CreateClient(); c.DefaultRequestHeaders.Authorization=new("Bearer",f.Token("Admin"));
        foreach(var q in new[]{"page=0","pageSize=101","supplierId=0","status=99","sortBy=totalAmount","sortDirection=bad","from=2026-01-02T00:00:00Z&to=2026-01-01T00:00:00Z"})
            Assert.Equal(HttpStatusCode.BadRequest,(await c.GetAsync("/api/v1/purchase-receipts?"+q)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/v1/purchase-receipts/1/cancel",new{})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PutAsJsonAsync("/api/v1/purchase-receipts/1",new{supplierId=1})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/v1/purchase-receipts",new{supplierId=1,lines=new[]{new{itemId=1,quantity=1,unitPrice=1,lineTotal=1}}})).StatusCode);
        Assert.Equal(0,f.Calls);
        Assert.Equal(HttpStatusCode.OK,(await c.GetAsync("/api/v1/purchase-receipts?supplierId=2&status=2&page=2&pageSize=3&sortBy=id&sortDirection=asc&from=2026-01-01T00:00:00Z&to=2026-02-01T00:00:00Z")).StatusCode);
        Assert.Equal(2,f.LastQuery!.SupplierId); Assert.Equal(DocumentStatus.Cancelled,f.LastQuery.Status); Assert.Equal(3,f.LastQuery.PageSize); Assert.Equal("id",f.LastQuery.SortBy);
    }
    [Theory]
    [InlineData(400)][InlineData(404)][InlineData(409)]
    public async Task ExpectedServiceErrorsBecomeProblemDetails(int status)
    {
        await using var f=new PurchaseReceiptsApiFactory{ErrorStatus=status}; using var c=f.CreateClient(); c.DefaultRequestHeaders.Authorization=new("Bearer",f.Token("Admin"));
        foreach(var response in new[]{await c.GetAsync("/api/v1/purchase-receipts/1"),await c.PostAsJsonAsync("/api/v1/purchase-receipts/1/cancel",new{rowVersion="AAAAAAAAAAA="})})
        { Assert.Equal(status,(int)response.StatusCode); Assert.Equal("application/problem+json",response.Content.Headers.ContentType!.MediaType); Assert.DoesNotContain("stackTrace",await response.Content.ReadAsStringAsync()); }
    }
}
