using System.Collections.Concurrent;
using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BttMiniERP.Tests.Fixtures;

public sealed class BootstrapSqlFactAttribute : FactAttribute
{
    public BootstrapSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BTT_RUN_SQL_TESTS") != "1"
            || Environment.GetEnvironmentVariable("BTT_RUN_BOOTSTRAP_SQL_TESTS") != "1")
            Skip = "Bootstrap SQL cần phê duyệt riêng; không chạy trên database có dữ liệu Identity.";
    }
}

[CollectionDefinition("Bootstrap SQL", DisableParallelization = true)]
public sealed class BootstrapSqlCollection;

// Không dùng AuthSqlFixture vì fixture đó seed role trước test và che mất công việc của seeder.
// Tái sử dụng guard/settings/schema verifier; mỗi test có baseline rỗng và ownership riêng.
public sealed class BootstrapSqlFixture : IAsyncDisposable
{
    public const string Password = "Bootstrap-Test-Only!5834";
    private readonly string connection;
    private readonly OwnedWrites ownership = new();
    private bool approvedEmptyBaseline;
    private BootstrapSqlFixture(string connection) => this.connection = connection;

    public static SqlServerTestSettings ReadSettings(Func<string, string?> read)
    {
        if (read("BTT_RUN_SQL_TESTS") != "1" || read("BTT_RUN_BOOTSTRAP_SQL_TESTS") != "1"
            || read("BTT_SQL_TEST_PROFILE") != "Bootstrap"
            || read("BTT_SQL_TEST_ENVIRONMENT") != "Testing"
            || read("BTT_SQL_APPROVED_SERVER") != "localhost"
            || read("BTT_SQL_VERIFICATION_STAGE") != "After"
            || read("BTT_BOOTSTRAP_SQL_APPROVED_OPERATIONS") != "Bootstrap,TemporaryIdentityWrites,CleanupOwnedRecords")
            throw new InvalidOperationException("Chưa duyệt bootstrap SQL tests.");
        var raw = read("BTT_SQL_TEST_CONNECTION_STRING");
        ValidateConnection(raw);
        return new SqlServerTestSettings { ConnectionString = raw!, Profile = "Bootstrap",
            DatabaseName = DatabaseName, Stage = "After" };
    }

    public const string DatabaseName = "BttMiniERP_BootstrapTest";

    internal static void ValidateConnection(string? raw)
    {
        SqlConnectionStringBuilder sql;
        try { sql = new(raw ?? ""); }
        catch (ArgumentException) { throw new InvalidOperationException("Cấu hình Bootstrap SQL không hợp lệ."); }
        if (sql.InitialCatalog != DatabaseName || sql.DataSource != "localhost"
            || !sql.IntegratedSecurity || sql.UserID.Length != 0 || sql.Password.Length != 0
            || sql.AttachDBFilename.Length != 0 || sql.UserInstance || sql.FailoverPartner.Length != 0
            || sql.ApplicationIntent != ApplicationIntent.ReadWrite)
            throw new InvalidOperationException("Bootstrap chỉ cho localhost/default/Windows/BttMiniERP_BootstrapTest; không attach/failover.");
    }

    public static void RequireEmpty(IEnumerable<IdentityDatabaseVerifier.TableFingerprint> tables)
    {
        string[] expected = ["AspNetUsers", "AspNetRoles", "AspNetUserRoles", "AspNetUserClaims",
            "AspNetRoleClaims", "AspNetUserLogins", "AspNetUserTokens"];
        var snapshot = tables.ToArray();
        if (!snapshot.Select(table => table.Table).Order().SequenceEqual(expected.Order())
            || snapshot.Any(table => table.Rows != 0))
            throw new InvalidOperationException("Bootstrap SQL cần Identity rỗng; không xóa/reset dữ liệu có sẵn để chạy test.");
    }

    public static async Task<BootstrapSqlFixture> OpenAsync()
    {
        var settings = ReadSettings(Environment.GetEnvironmentVariable); // guard trước mở connection
        var fixture = new BootstrapSqlFixture(settings.ConnectionString);
        using var provider = fixture.CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.OpenConnectionAsync();
        var target = await IdentityDatabaseVerifier.ReadRowsAsync(db,
            "SELECT DB_NAME(), CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')), COALESCE(CONVERT(nvarchar(128),SERVERPROPERTY('InstanceName')),N'')");
        Assert.True(target.SequenceEqual(new[] { $"{DatabaseName}|{Environment.MachineName}|" }), "Sai instance/database thực tế.");
        await IdentityDatabaseVerifier.AssertSchemaAsync(db, 128);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        RequireEmpty(await IdentityDatabaseVerifier.CaptureDataAsync(db));
        fixture.approvedEmptyBaseline = true;
        return fixture;
    }

    public ServiceProvider CreateProvider(IInterceptor? interceptor = null, bool pendingMigration = false,
        IInterceptor? additionalInterceptor = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:DefaultConnection"] = connection }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        ValidateConnection(connection);
        // Đăng ký test riêng; không nới guard runtime Dev/Test. Identity options dùng registration chung bên dưới.
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connection));
        services.AddApplicationSecurity(config); // không resolve TokenService/JWT, không đọc secrets
        services.ConfigureDbContext<ApplicationDbContext>(options =>
        {
            options.AddInterceptors(ownership);
            if (interceptor != null) options.AddInterceptors(interceptor);
            if (additionalInterceptor != null) options.AddInterceptors(additionalInterceptor);
            if (pendingMigration) options.ReplaceService<IMigrationsAssembly, BootstrapPendingMigrationsAssembly>();
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public static IConfiguration Credentials(string name, string? password = Password)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["BootstrapAdmin:UserName"] = name, ["BootstrapAdmin:Password"] = password }).Build();

    public static string NewName() => "bootstrap_test_" + Guid.NewGuid().ToString("N");

    public async Task<IdentityDatabaseVerifier.TableFingerprint[]> SnapshotAsync()
    {
        using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.OpenConnectionAsync();
        return await IdentityDatabaseVerifier.CaptureDataAsync(db);
    }

    public async ValueTask DisposeAsync()
    {
        if (!approvedEmptyBaseline) return;
        using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userIds = ownership.Users.Keys.ToArray();
        var roleIds = ownership.Roles.Keys.ToArray();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await IdentityAdministrationLock.AcquireAsync(db, default);
            await db.UserTokens.Where(x => userIds.Contains(x.UserId)).ExecuteDeleteAsync();
            await db.UserLogins.Where(x => userIds.Contains(x.UserId)).ExecuteDeleteAsync();
            await db.UserClaims.Where(x => userIds.Contains(x.UserId)).ExecuteDeleteAsync();
            await db.UserRoles.Where(x => userIds.Contains(x.UserId)).ExecuteDeleteAsync();
            await db.RoleClaims.Where(x => roleIds.Contains(x.RoleId)).ExecuteDeleteAsync();
            await db.Users.Where(x => userIds.Contains(x.Id)).ExecuteDeleteAsync();
            await db.Roles.Where(x => roleIds.Contains(x.Id)).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }
        await db.Database.OpenConnectionAsync();
        RequireEmpty(await IdentityDatabaseVerifier.CaptureDataAsync(db));
        await IdentityDatabaseVerifier.AssertSchemaAsync(db, 128); // history/schema không đổi
    }

    private sealed class OwnedWrites : SaveChangesInterceptor
    {
        public ConcurrentDictionary<string, byte> Users { get; } = new();
        public ConcurrentDictionary<string, byte> Roles { get; } = new();
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            // Ghi ID trước Save: vẫn cleanup được nếu commit thành công nhưng client mất kết nối.
            foreach (var entry in eventData.Context!.ChangeTracker.Entries<ApplicationUser>().Where(e => e.State == EntityState.Added))
                Users.TryAdd(entry.Entity.Id, 0);
            foreach (var entry in eventData.Context.ChangeTracker.Entries<IdentityRole>().Where(e => e.State == EntityState.Added))
                Roles.TryAdd(entry.Entity.Id, 0);
            return ValueTask.FromResult(result);
        }
    }
}
