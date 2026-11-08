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

public sealed class MasterDataSqlFactAttribute : FactAttribute
{
    public MasterDataSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BTT_RUN_SQL_TESTS") != "1"
            || Environment.GetEnvironmentVariable("BTT_RUN_MASTERDATA_SQL_TESTS") != "1")
            Skip = "P2.1 SQL chưa được duyệt; cần database MasterDataTest riêng và opt-in.";
    }
}

[CollectionDefinition("MasterData SQL", DisableParallelization = true)]
public sealed class MasterDataSqlCollection;

// Database mới cho nghiệp vụ, tuyệt đối không dùng profile Identity/Dev/Test/Bootstrap.
// Không migrate/seed role/password. Chỉ actor tạm không credentials và graph test có ownership.
public sealed class MasterDataSqlFixture : IAsyncDisposable
{
    public const string DatabaseName = "BttMiniERP_MasterDataTest";
    private readonly string connection;
    private readonly OwnedWrites ownership = new();
    private ApplicationDbContext? guard;
    private bool mayCleanup;
    private bool ownsSessionLock;
    public string ActorId { get; } = "p21-" + Guid.NewGuid().ToString("N");
    internal string Connection => connection;
    private MasterDataSqlFixture(string connection) => this.connection = connection;

    public static string ReadSettings(Func<string, string?> read)
    {
        if (read("BTT_RUN_SQL_TESTS") != "1" || read("BTT_RUN_MASTERDATA_SQL_TESTS") != "1"
            || read("BTT_SQL_TEST_PROFILE") != "MasterData" || read("BTT_SQL_TEST_ENVIRONMENT") != "Testing"
            || read("BTT_SQL_APPROVED_SERVER") != "localhost"
            || read("BTT_MASTERDATA_SQL_APPROVED_OPERATIONS") != "Verify,TemporaryWrites,Concurrency,CleanupOwnedRecords")
            throw new InvalidOperationException("Chưa duyệt profile SQL P2.1.");
        SqlConnectionStringBuilder sql;
        try { sql = new(read("BTT_SQL_TEST_CONNECTION_STRING") ?? ""); }
        catch (ArgumentException) { throw new InvalidOperationException("Cấu hình SQL P2.1 không hợp lệ."); }
        if (sql.DataSource != "localhost" || sql.InitialCatalog != DatabaseName || !sql.IntegratedSecurity
            || sql.UserID.Length != 0 || sql.Password.Length != 0 || sql.AttachDBFilename.Length != 0
            || sql.UserInstance || sql.FailoverPartner.Length != 0 || sql.ApplicationIntent != ApplicationIntent.ReadWrite
            || sql.Encrypt == SqlConnectionEncryptOption.Optional)
            throw new InvalidOperationException("Chỉ localhost/default/Windows/MasterDataTest, mã hóa và không attach/failover.");
        return sql.ConnectionString;
    }

    public ApplicationDbContext Context(params IInterceptor[] interceptors)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection)
            .AddInterceptors(interceptors.Prepend(ownership)).Options);

    public CategoryService Categories(ApplicationDbContext db) => new(db, Actor(), TimeProvider.System);
    public ItemService Items(ApplicationDbContext db) => new(db, Actor(), TimeProvider.System);
    private CurrentUser Actor() => new(new HttpContextAccessor { HttpContext = new DefaultHttpContext
        { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", ActorId)], "Test")) } });

    public static async Task<MasterDataSqlFixture> OpenAsync()
    {
        var fixture = new MasterDataSqlFixture(ReadSettings(Environment.GetEnvironmentVariable));
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
            await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'BttMiniERP.P21.Tests', @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=0; IF @r<0 THROW 51021, 'P2.1 test session already active', 1;");
            fixture.ownsSessionLock = true;
            Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            BootstrapSqlFixture.RequireEmpty(await IdentityDatabaseVerifier.CaptureDataAsync(db));
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
                var items = ownership.Items.Keys.ToArray();
                var categories = ownership.Categories.Keys.ToArray();
                await db.Items.Where(x => items.Contains(x.Id) && x.CreatedByUserId == ActorId).ExecuteDeleteAsync();
                await db.Categories.Where(x => categories.Contains(x.Id) && x.CreatedByUserId == ActorId).ExecuteDeleteAsync();
                await db.Users.Where(x => x.Id == ActorId).ExecuteDeleteAsync();
                await transaction.CommitAsync();
                Assert.False(await db.Items.AnyAsync(x => x.CreatedByUserId == ActorId));
                Assert.False(await db.Categories.AnyAsync(x => x.CreatedByUserId == ActorId));
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
                        await guard.Database.ExecuteSqlRawAsync("EXEC sys.sp_releaseapplock @Resource=N'BttMiniERP.P21.Tests', @LockOwner=N'Session';");
                }
                finally { await guard.DisposeAsync(); }
            }
        }
    }

    private sealed class OwnedWrites : SaveChangesInterceptor
    {
        public ConcurrentDictionary<int, bool> Categories { get; } = new();
        public ConcurrentDictionary<int, bool> Items { get; } = new();
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken cancellationToken = default)
        {
            foreach (var entry in data.Context!.ChangeTracker.Entries<Category>().Where(x => x.Entity.Id > 0)) Categories.TryAdd(entry.Entity.Id, true);
            foreach (var entry in data.Context.ChangeTracker.Entries<Item>().Where(x => x.Entity.Id > 0)) Items.TryAdd(entry.Entity.Id, true);
            return ValueTask.FromResult(result);
        }
    }
}
