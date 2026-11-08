using System.Security.Cryptography;
using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace BttMiniERP.Tests.Fixtures;

public class ApiFactory : WebApplicationFactory<Program>
{
    public JwtSettings Jwt { get; } = new() { Issuer = "btt-tests", Audience = "btt-test-client",
        SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)), LifetimeMinutes = 15 };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // HTTP tests không đọc/ghi key ring của tài khoản Windows đang chạy IDE.
        builder.ConfigureServices(services => services.AddDataProtection().UseEphemeralDataProtectionProvider());
        builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole());
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = Jwt.Issuer, ["Jwt:Audience"] = Jwt.Audience,
            ["Jwt:SigningKey"] = Jwt.SigningKey, ["Jwt:LifetimeMinutes"] = "15"
        }));
    }

    public string Token(params string[] roles) => new TokenService(Options.Create(Jwt), TimeProvider.System)
        .Create(new ApplicationUser { Id = "test-actor", UserName = "test-actor" }, roles).AccessToken;
}
