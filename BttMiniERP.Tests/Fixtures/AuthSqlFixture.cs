using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BttMiniERP.Tests.Fixtures;

public sealed class AuthSqlFactAttribute : FactAttribute
{
    public AuthSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BTT_RUN_SQL_TESTS") != "1"
            || Environment.GetEnvironmentVariable("BTT_RUN_AUTH_SQL_TESTS") != "1")
            Skip = "P1.4 SQL cần phê duyệt riêng và schema 128; chưa chạy.";
    }
}

[CollectionDefinition("Auth SQL", DisableParallelization = true)]
public sealed class AuthSqlCollection : ICollectionFixture<AuthSqlFixture>;

public sealed class AuthSqlFixture : IAsyncLifetime
{
    public const string Password = "Temporary-Auth-Tests!7382";
    private string? connection;
    private bool isolatedPhase1;
    private readonly ConcurrentBag<string> ownedUsers = [];
    private readonly List<string> ownedRoles = [];
    public string Connection => connection ?? throw new InvalidOperationException("Auth SQL chưa được bật.");

    public ServiceProvider CreateProvider(IInterceptor? interceptor = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = Connection,
            ["Jwt:Issuer"] = "auth-sql-tests", ["Jwt:Audience"] = "auth-sql-client",
            ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        if (isolatedPhase1)
        {
            BootstrapSqlFixture.ValidateConnection(Connection);
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(Connection));
        }
        else services.AddPersistence(config, "Testing");
        services.AddApplicationSecurity(config);
        if (interceptor != null) services.ConfigureDbContext<ApplicationDbContext>(options => options.AddInterceptors(interceptor));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("BTT_RUN_SQL_TESTS") != "1"
            || Environment.GetEnvironmentVariable("BTT_RUN_AUTH_SQL_TESTS") != "1") return;
        if (Environment.GetEnvironmentVariable("BTT_AUTH_SQL_APPROVED_OPERATIONS") != "TemporaryIdentityWrites,Concurrency,CleanupOwnedRecords")
            throw new InvalidOperationException("Chưa duyệt thao tác SQL Identity P1.4.");
        var settings = SqlServerTestSettings.Read(Environment.GetEnvironmentVariable);
        isolatedPhase1 = settings.Profile == "Phase1" && settings.Stage == "After"
            && settings.DatabaseName == BootstrapSqlFixture.DatabaseName;
        if (!isolatedPhase1 && (settings.Profile != "Default" || settings.DatabaseName != "BttMiniERP_Test"))
            throw new InvalidOperationException("Auth SQL chỉ cho BttMiniERP_Test riêng, không UpCheck/DownCheck/Dev.");
        connection = settings.ConnectionString;
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.OpenConnectionAsync();
        Assert.Equal(settings.DatabaseName, context.Database.GetDbConnection().Database);
        if (isolatedPhase1)
        {
            await SqlServerFixture.VerifyPhase1TargetAsync(context);
            BootstrapSqlFixture.RequireEmpty(await IdentityDatabaseVerifier.CaptureDataAsync(context));
        }
        await IdentityDatabaseVerifier.AssertSchemaAsync(context, 128);
        if (await context.Users.AnyAsync()) throw new InvalidOperationException("Auth SQL cần database kiểm thử chưa có user; không reset/xóa dữ liệu sẵn có.");
        var manager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var roleName in AppRoles.All)
        {
            if (await manager.RoleExistsAsync(roleName)) continue;
            var role = new IdentityRole(roleName);
            Assert.True((await manager.CreateAsync(role)).Succeeded);
            ownedRoles.Add(role.Id);
        }
    }

    public void Track(string id) => ownedUsers.Add(id);

    public async Task<ApplicationUser> CreateUserAsync(IServiceProvider provider, params string[] roles)
    {
        var manager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "p14_" + Guid.NewGuid().ToString("N"), LockoutEnabled = true };
        Track(user.Id);
        Assert.True((await manager.CreateAsync(user, Password)).Succeeded);
        Assert.True((await manager.AddToRolesAsync(user, roles)).Succeeded);
        return user;
    }

    public static void SetActor(IServiceProvider provider, string id)
        => provider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id), new Claim("role", "Admin")], "Bearer")) };

    public async Task CleanupUsersAsync()
    {
        if (connection == null) return;
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ids = ownedUsers.ToArray();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await context.UserRoles.Where(link => ids.Contains(link.UserId)).ExecuteDeleteAsync();
        await context.Users.Where(user => ids.Contains(user.Id)).ExecuteDeleteAsync();
        await transaction.CommitAsync();
    }

    public async Task DisposeAsync()
    {
        if (connection == null) return;
        await CleanupUsersAsync();
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Roles.Where(role => ownedRoles.Contains(role.Id)).ExecuteDeleteAsync();
    }
}
