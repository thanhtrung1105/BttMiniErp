using System.Net;
using BttMiniERP.Api.Data;
using BttMiniERP.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace BttMiniERP.Tests.Integration.Inventory;

public sealed class InventoryQueryHttpTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("", HttpStatusCode.Forbidden)]
    [InlineData("Admin", HttpStatusCode.BadRequest)]
    [InlineData("Manager", HttpStatusCode.BadRequest)]
    [InlineData("PurchasingStaff", HttpStatusCode.BadRequest)]
    [InlineData("SalesStaff", HttpStatusCode.BadRequest)]
    [InlineData("ProductionStaff", HttpStatusCode.BadRequest)]
    [InlineData("WarehouseStaff", HttpStatusCode.BadRequest)]
    [InlineData("Viewer", HttpStatusCode.BadRequest)]
    public async Task AuthorizationPrecedesQueryValidation(string? role, HttpStatusCode expected)
    {
        await using var factory = new OfflineFactory();
        using var client = factory.CreateClient();
        if (role != null) client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token(role == "" ? [] : [role]));
        // Query sai có chủ đích: role hợp lệ tới MVC validation mà không cần mở SQL.
        foreach (var route in new[] { "balances?pageSize=0", "transactions?from=bad-date" })
        {
            using var result = await client.GetAsync("/api/v1/inventory/" + route);
            Assert.Equal(expected, result.StatusCode);
            if (expected == HttpStatusCode.BadRequest)
                Assert.Equal("application/problem+json", result.Content.Headers.ContentType!.MediaType);
        }
    }

    [Fact]
    public void IssuerRejectsAnUnlistedRoleBeforeAnInventoryRequestCanBeSent()
    {
        using var factory = new OfflineFactory();
        Assert.Throws<BttMiniERP.Api.Common.ApiException>(() => factory.Token("unlisted"));
    }

    private sealed class OfflineFactory : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddScoped(_ =>
                new ApplicationDbContextFactory().CreateDbContext(["--offline"])));
        }
    }
}
