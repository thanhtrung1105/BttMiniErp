using System.Collections.Concurrent;
using System.Security.Claims;
using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BttMiniERP.Tests.Fixtures;

public sealed class PartnersSqlFactAttribute : FactAttribute
{
    public PartnersSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BTT_RUN_SQL_TESTS") != "1"
            || Environment.GetEnvironmentVariable("BTT_RUN_PARTNERS_SQL_TESTS") != "1")
            Skip = "P2.2 SQL chưa được duyệt; cần database PartnersTest riêng và opt-in.";
    }
}

[CollectionDefinition("Partners SQL", DisableParallelization = true)]
public sealed class PartnersSqlCollection;

// Database mới cho nghiệp vụ, tuyệt đối không dùng profile Identity/Dev/Test/Bootstrap.
// Không migrate/seed role/password. Chỉ actor tạm không credentials và graph test có ownership.
public sealed class PartnersSqlFixture : IAsyncDisposable
{
    public const string DatabaseName = "BttMiniERP_PartnersTest";
    private readonly string connection;
    private readonly OwnedWrites ownership = new();
    private ApplicationDbContext? guard;
    private bool mayCleanup;
    private bool ownsSessionLock;
    public string ActorId { get; } = "p22-" + Guid.NewGuid().ToString("N");
    internal string Connection => connection;
    private PartnersSqlFixture(string connection) => this.connection = connection;

    public static string ReadSettings(Func<string, string?> read)
    {
        if (read("BTT_RUN_SQL_TESTS") != "1" || read("BTT_RUN_PARTNERS_SQL_TESTS") != "1"
            || read("BTT_SQL_TEST_PROFILE") != "Partners" || read("BTT_SQL_TEST_ENVIRONMENT") != "Testing"
            || read("BTT_SQL_APPROVED_SERVER") != "localhost"
            || read("BTT_PARTNERS_SQL_APPROVED_OPERATIONS") != "Verify,TemporaryWrites,Concurrency,CleanupOwnedRecords")
            throw new InvalidOperationException("Chưa duyệt profile SQL P2.2.");
        SqlConnectionStringBuilder sql;
        try { sql = new(read("BTT_SQL_TEST_CONNECTION_STRING") ?? ""); }
        catch (ArgumentException) { throw new InvalidOperationException("Cấu hình SQL P2.2 không hợp lệ."); }
        if (sql.DataSource != "localhost" || sql.InitialCatalog != DatabaseName || !sql.IntegratedSecurity
            || sql.UserID.Length != 0 || sql.Password.Length != 0 || sql.AttachDBFilename.Length != 0
            || sql.UserInstance || sql.FailoverPartner.Length != 0 || sql.ApplicationIntent != ApplicationIntent.ReadWrite
            || sql.Encrypt == SqlConnectionEncryptOption.Optional)
            throw new InvalidOperationException("Chỉ localhost/default/Windows/PartnersTest, mã hóa và không attach/failover.");
        return sql.ConnectionString;
    }

    public ApplicationDbContext Context(params IInterceptor[] interceptors)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection)
            .AddInterceptors(interceptors.Prepend(ownership)).Options);

    public SupplierService Suppliers(ApplicationDbContext db) => new(db, Actor(), TimeProvider.System);
    public CustomerService Customers(ApplicationDbContext db) => new(db, Actor(), TimeProvider.System);
    private CurrentUser Actor() => new(new HttpContextAccessor { HttpContext = new DefaultHttpContext
        { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", ActorId)], "Test")) } });

    public static async Task<PartnersSqlFixture> OpenAsync()
    {
        var fixture = new PartnersSqlFixture(ReadSettings(Environment.GetEnvironmentVariable));
        try
        {
            fixture.guard = fixture.Context();
            var db = fixture.guard;
            await db.Database.OpenConnectionAsync();
            var actual = await IdentityDatabaseVerifier.ReadRowsAsync(db,
                "SELECT DB_NAME(), CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')), COALESCE(CONVERT(nvarchar(128),SERVERPROPERTY('InstanceName')),N''), CONVERT(nvarchar(20),CONNECTIONPROPERTY('auth_scheme'))");
            Assert.True(actual.Count == 1 && (actual[0] == $"{DatabaseName}|{Environment.MachineName}||NTLM"
                || actual[0] == $"{DatabaseName}|{Environment.MachineName}||KERBEROS"), "Sai target SQL thực tế.");
            // Session lock giữ tới Dispose: ngăn hai runner cùng nhận baseline rỗng.
            await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'BttMiniERP.P22.Tests', @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=0; IF @r<0 THROW 51021, 'P2.2 test session already active', 1;");
            fixture.ownsSessionLock = true;
            Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            BootstrapSqlFixture.RequireEmpty(await IdentityDatabaseVerifier.CaptureDataAsync(db));
            Assert.False(await db.Suppliers.AnyAsync());
            Assert.False(await db.Customers.AnyAsync());
            Assert.False(await db.Categories.AnyAsync());
            Assert.False(await db.Items.AnyAsync());
            fixture.mayCleanup = true;
            db.Users.Add(new ApplicationUser { Id = fixture.ActorId, UserName = fixture.ActorId, NormalizedUserName = fixture.ActorId.ToUpperInvariant() });
            await db.SaveChangesAsync();
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (mayCleanup)
            {
                await using var db = Context();
                await using var transaction = await db.Database.BeginTransactionAsync();
                var customerIds = ownership.Customers.Keys.ToArray();
                var supplierIds = ownership.Suppliers.Keys.ToArray();
                await db.Customers.Where(x => customerIds.Contains(x.Id) && x.CreatedByUserId == ActorId).ExecuteDeleteAsync();
                await db.Suppliers.Where(x => supplierIds.Contains(x.Id) && x.CreatedByUserId == ActorId).ExecuteDeleteAsync();
                await db.Users.Where(x => x.Id == ActorId).ExecuteDeleteAsync();
                await transaction.CommitAsync();
                Assert.False(await db.Customers.AnyAsync(x => x.CreatedByUserId == ActorId));
                Assert.False(await db.Suppliers.AnyAsync(x => x.CreatedByUserId == ActorId));
                Assert.False(await db.Users.AnyAsync(x => x.Id == ActorId));
            }
        }
        finally
        {
            if (guard != null)
            {
                try
                {
                    if (ownsSessionLock && guard.Database.GetDbConnection().State == System.Data.ConnectionState.Open)
                        await guard.Database.ExecuteSqlRawAsync("EXEC sys.sp_releaseapplock @Resource=N'BttMiniERP.P22.Tests', @LockOwner=N'Session';");
                }
                finally { await guard.DisposeAsync(); }
            }
        }
    }

    private sealed class OwnedWrites : SaveChangesInterceptor
    {
        public ConcurrentDictionary<int, bool> Suppliers { get; } = new();
        public ConcurrentDictionary<int, bool> Customers { get; } = new();
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken cancellationToken = default)
        {
            foreach (var entry in data.Context!.ChangeTracker.Entries<Supplier>().Where(x => x.Entity.Id > 0)) Suppliers.TryAdd(entry.Entity.Id, true);
            foreach (var entry in data.Context.ChangeTracker.Entries<Customer>().Where(x => x.Entity.Id > 0)) Customers.TryAdd(entry.Entity.Id, true);
            return ValueTask.FromResult(result);
        }
    }
}
