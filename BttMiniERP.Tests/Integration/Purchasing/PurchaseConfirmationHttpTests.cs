using System.Net;
using System.Net.Http.Json;
using System.Text;
using BttMiniERP.Api.DTOs.PurchaseReceipts;
using BttMiniERP.Api.Enums;
using BttMiniERP.Tests.Fixtures;

namespace BttMiniERP.Tests.Integration.Purchasing;

public sealed class PurchaseConfirmationHttpTests
{
    private const string Route = "/api/v1/purchase-receipts/1/confirm";
    [Theory]
    [InlineData(null,401)] [InlineData("Admin",200)] [InlineData("Manager",200)]
    [InlineData("PurchasingStaff",200)] [InlineData("WarehouseStaff",200)]
    [InlineData("SalesStaff",403)] [InlineData("ProductionStaff",403)] [InlineData("Viewer",403)]
    public async Task OnlyApprovedRolesCanConfirm(string? role, int status)
    {
        await using var f = new PurchaseReceiptsApiFactory(); using var client = f.CreateClient();
        if (role != null) client.DefaultRequestHeaders.Authorization = new("Bearer", f.Token(role));
        var response = await client.PostAsJsonAsync(Route, new { rowVersion = "AAAAAAAAAAA=" });
        Assert.Equal(status, (int)response.StatusCode); Assert.Equal(status == 200 ? 1 : 0, f.Calls);
        if (status == 200) { var dto = (await response.Content.ReadFromJsonAsync<PurchaseReceiptResponse>())!;
            Assert.Equal(DocumentStatus.Confirmed, dto.Status); Assert.NotNull(dto.ConfirmedAt); Assert.Equal("test-actor", dto.ConfirmedByUserId); }
    }
    [Theory]
    [InlineData("{}")] [InlineData("{\"rowVersion\":\"bad\"}")]
    [InlineData("{\"rowVersion\":\"AAAAAAAAAAA=\",\"confirmedByUserId\":\"spoof\"}")]
    [InlineData("{\"rowVersion\":\"AAAAAAAAAAA=\",\"confirmedAt\":\"2026-01-01T00:00:00Z\"}")]
    [InlineData("{\"rowVersion\":\"AAAAAAAAAAA=\",\"totalAmount\":1}")]
    [InlineData("{\"rowVersion\":\"AAAAAAAAAAA=\",\"status\":1}")]
    [InlineData("{\"rowVersion\":\"AAAAAAAAAAA=\",\"quantityOnHand\":1}")]
    public async Task InvalidOrServerManagedFieldsNeverReachService(string json)
    {
        await using var f = new PurchaseReceiptsApiFactory(); using var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", f.Token("Admin"));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(Route, new StringContent(json, Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(0, f.Calls);
    }
    [Theory] [InlineData(404)] [InlineData(409)]
    public async Task ServiceErrorsUseProblemDetails(int status)
    {
        await using var f = new PurchaseReceiptsApiFactory { ErrorStatus = status }; using var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", f.Token("Admin"));
        var response = await client.PostAsJsonAsync(Route, new { rowVersion = "AAAAAAAAAAA=" });
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.DoesNotContain("stackTrace", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }
}
