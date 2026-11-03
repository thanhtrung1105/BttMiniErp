using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BttMiniERP.Tests.Integration;

public sealed class ProblemDetailsTests
{
    [Fact]
    public async Task UnhandledException_ReturnsProblemDetailsWithoutExceptionMessage()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                // Test không phụ thuộc quyền ghi Windows Event Log.
                builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole());
                builder.ConfigureServices(services =>
                    services.AddSingleton<IStartupFilter, ThrowingStartupFilter>());
            });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        using var response = await client.GetAsync("/__test/unhandled-error");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(500, document.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("title").GetString()));
        Assert.DoesNotContain("Test-only exception detail", body);
    }

    // Chỉ thêm nhánh gây lỗi trong test host, không tạo endpoint thử lỗi ở API thật.
    private sealed class ThrowingStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                next(app);
                app.Run(_ => throw new InvalidOperationException("Test-only exception detail"));
            };
        }
    }
}
