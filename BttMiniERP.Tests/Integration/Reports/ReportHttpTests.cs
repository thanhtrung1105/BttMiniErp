using System.Net;
using BttMiniERP.Api.Data;
using BttMiniERP.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace BttMiniERP.Tests.Integration.Reports;

public sealed class ReportHttpTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("Viewer")]
    [InlineData("WarehouseStaff")]
    [InlineData("PurchasingStaff")]
    [InlineData("SalesStaff")]
    [InlineData("ProductionStaff")]
    public async Task RolePolicyIsCheckedBeforeRequiredPeriod(string? role)
    {
        await using var factory = new OfflineFactory();
        using var client = factory.CreateClient();
        if (role != null) client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token(role));
        foreach (var report in new[] { "purchases", "sales", "inventory", "production" })
        {
            bool allowed = role is "Admin" or "Manager" or "Viewer" || role == "WarehouseStaff" && report == "inventory";
            using var result = await client.GetAsync("/api/v1/reports/" + report);
            Assert.Equal(role == null ? HttpStatusCode.Unauthorized : allowed ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden,
                result.StatusCode);
            if (allowed) Assert.Equal("application/problem+json", result.Content.Headers.ContentType!.MediaType);
        }
    }

    private sealed class OfflineFactory : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.AddScoped(_ => new ApplicationDbContextFactory().CreateDbContext(["--offline"])));
        }
    }
}
