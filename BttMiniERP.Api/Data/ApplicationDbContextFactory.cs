using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using BttMiniERP.Api.Data.Configurations;
using Microsoft.AspNetCore.Identity;

namespace BttMiniERP.Api.Data;

public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    // Provider chỉ chứa options, dùng chung trong tiến trình tooling; không chứa connection/secret.
    private static readonly IServiceProvider DesignTimeServices = new ServiceCollection()
        .Configure<IdentityOptions>(IdentityPersistenceConfiguration.ConfigureOptions)
        .BuildServiceProvider();

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        if (args.Contains("--offline", StringComparer.Ordinal))
        {
            // Chỉ tạo model/migration/script; đích giả, không trỏ instance người dùng.
            return new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseApplicationServiceProvider(DesignTimeServices)
                .UseSqlServer("Server=127.0.0.1,1;Database=BttMiniERP_Dev;Integrated Security=True;Encrypt=True;Connect Timeout=1")
                .Options);
        }

        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<ApplicationDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connection = DatabaseConnectionSettings.Validate(
            configuration.GetConnectionString("DefaultConnection"), environment);
        return new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseApplicationServiceProvider(DesignTimeServices)
            .UseSqlServer(connection).Options);
    }
}
