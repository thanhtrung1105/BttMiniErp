using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using BttMiniERP.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BttMiniERP.Tests.Integration;

public sealed class ProblemDetailsTests
{
    [Fact]
    public async Task UnhandledException_ReturnsProblemDetailsWithoutExceptionMessage()
    {
        var logs = new CapturedLogs();
        await using var baseFactory = new ApiFactory();
        await using var factory = baseFactory
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                // Test không phụ thuộc quyền ghi Windows Event Log.
                builder.ConfigureLogging(logging => logging.ClearProviders().AddProvider(logs));
                builder.ConfigureServices(services =>
                    services.AddSingleton<IStartupFilter, ThrowingStartupFilter>());
            });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", baseFactory.Token("Viewer"));
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        using var response = await client.GetAsync("/__test/unhandled-error");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(500, document.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("title").GetString()));
        Assert.DoesNotContain("Test-only exception detail", body);
        var traceId = document.RootElement.GetProperty("traceId").GetString();
        var logged = Assert.Single(logs.Entries, entry => entry.EventId == 1400);
        Assert.Equal(LogLevel.Error, logged.Level);
        Assert.Contains(traceId!, logged.Message);
        Assert.Contains(typeof(InvalidOperationException).FullName!, logged.Message);
        Assert.DoesNotContain("Test-only exception detail", string.Join("\n", logs.Entries.Select(entry => entry.Message)));
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

    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, int EventId, string Message)> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this);
        public void Dispose() { }

        private sealed class CaptureLogger(CapturedLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => owner.Entries.Enqueue((logLevel, eventId.Id, formatter(state, exception) + exception?.ToString()));
        }
    }
}
