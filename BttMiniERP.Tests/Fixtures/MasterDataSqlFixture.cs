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
    public const string InventoryDatabaseName = "BttMiniERP_InventoryTest";
    public const string MigrationDatabaseName = "BttMiniERP_InventoryMigrationTest";
    public const string PurchasingDatabaseName = "BttMiniERP_PurchasingTest";
    public const string ConfirmationDatabaseName = "BttMiniERP_PurchaseConfirmationTest";
    public const string ProductionDatabaseName = "BttMiniERP_ProductionTest";
    public const string BomDatabaseName = "BttMiniERP_BomTest";
    private readonly string profile;
    private readonly string databaseName;
    private readonly string connection;
    private readonly OwnedWrites ownership = new();
    private readonly FixtureLockDiagnostics diagnostics = new();
    private ApplicationDbContext? guard;
    private bool mayCleanup;
    private bool ownsSessionLock;
    public string ActorId { get; } = "p21-" + Guid.NewGuid().ToString("N");
    internal string Connection => connection;
    private MasterDataSqlFixture(string connection, string profile) { this.connection = connection; this.profile = profile; databaseName = Target(profile); }
    private static string Target(string profile) => profile switch { "MasterData" => DatabaseName, "Inventory" => InventoryDatabaseName, "InventoryMigration" => MigrationDatabaseName, "Purchasing" => PurchasingDatabaseName, "Confirmation" => ConfirmationDatabaseName, "Bom" => BomDatabaseName, "Production" => ProductionDatabaseName, _ => throw new InvalidOperationException("Profile không hợp lệ.") };

    public static string ReadSettings(Func<string, string?> read, string profile = "MasterData")
    {
        var target = Target(profile);
        var flag = profile switch { "MasterData" => "MASTERDATA", "Inventory" => "INVENTORY", "Purchasing" => "PURCHASING", "Confirmation" => "CONFIRMATION", "Bom" => "BOM", "Production" => "PRODUCTION", _ => "INVENTORY_MIGRATION" };
        var operations = profile == "InventoryMigration" ? "Verify,MigrationUpRollback,TemporaryWrites,CleanupOwnedRecords" : "Verify,TemporaryWrites,Concurrency,CleanupOwnedRecords";
        if (read("BTT_RUN_SQL_TESTS") != "1" || read($"BTT_RUN_{flag}_SQL_TESTS") != "1"
            || read("BTT_SQL_TEST_PROFILE") != profile || read("BTT_SQL_TEST_ENVIRONMENT") != "Testing"
            || read("BTT_SQL_APPROVED_SERVER") != "localhost"
            || read($"BTT_{flag}_SQL_APPROVED_OPERATIONS") != operations)
            throw new InvalidOperationException("Chưa duyệt profile SQL danh mục/kho.");
        SqlConnectionStringBuilder sql;
        try { sql = new(read("BTT_SQL_TEST_CONNECTION_STRING") ?? ""); }
        catch (ArgumentException) { throw new InvalidOperationException("Cấu hình SQL danh mục/kho không hợp lệ."); }
        if (sql.DataSource != "localhost" || sql.InitialCatalog != target || !sql.IntegratedSecurity
            || sql.UserID.Length != 0 || sql.Password.Length != 0 || sql.AttachDBFilename.Length != 0
            || sql.MultipleActiveResultSets || sql.UserInstance || sql.FailoverPartner.Length != 0 || sql.ApplicationIntent != ApplicationIntent.ReadWrite
            || sql.Encrypt == SqlConnectionEncryptOption.Optional)
            throw new InvalidOperationException("Chỉ localhost/default/Windows và database của profile, mã hóa; không MARS/attach/failover.");
        return sql.ConnectionString;
    }

    public ApplicationDbContext Context(params IInterceptor[] interceptors)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection)
            .AddInterceptors(interceptors.Prepend(ownership).Prepend(diagnostics)).Options);

    public CategoryService Categories(ApplicationDbContext db) => new(db, Actor(), TimeProvider.System);
    public ItemService Items(ApplicationDbContext db) => new(db, Actor(), TimeProvider.System);
    public InventoryService Inventory(ApplicationDbContext db) => new(db, Actor(), TimeProvider.System);
    public ProductionOrderService Production(ApplicationDbContext db) => new(db, Actor(), TimeProvider.System, Inventory(db));
    public BillOfMaterialService Boms(ApplicationDbContext db) => new(db, Actor(), TimeProvider.System);
    public SupplierService Suppliers(ApplicationDbContext db) => new(db, Actor(), TimeProvider.System);
    public PurchaseReceiptService Receipts(ApplicationDbContext db) => new(db, Actor(), TimeProvider.System, Inventory(db));
    private CurrentUser Actor() => new(new HttpContextAccessor { HttpContext = new DefaultHttpContext
        { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", ActorId)], "Test")) } });

    public static async Task<MasterDataSqlFixture> OpenAsync(string profile = "MasterData")
    {
        var fixture = new MasterDataSqlFixture(ReadSettings(Environment.GetEnvironmentVariable, profile), profile);
        try
        {
            fixture.guard = fixture.Context();
            fixture.diagnostics.Mark("guard", fixture.guard);
            var db = fixture.guard;
            await db.Database.OpenConnectionAsync();
            var actual = await IdentityDatabaseVerifier.ReadRowsAsync(db,
                "SELECT DB_NAME(), CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')), COALESCE(CONVERT(nvarchar(128),SERVERPROPERTY('InstanceName')),N''), CONVERT(nvarchar(20),CONNECTIONPROPERTY('auth_scheme'))");
            Assert.True(actual.Count == 1 && (actual[0] == $"{fixture.databaseName}|{Environment.MachineName}||NTLM"
                || actual[0] == $"{fixture.databaseName}|{Environment.MachineName}||KERBEROS"), "Sai target SQL thực tế.");
            // Session lock giữ tới Dispose: ngăn hai runner cùng nhận baseline rỗng.
            await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'BttMiniERP.P21.Tests', @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=0; IF @r<0 THROW 51021, 'P2.1 test session already active', 1;");
            fixture.ownsSessionLock = true;
            var migrations = db.Database.GetMigrations().ToArray();
            Assert.EndsWith("_AddProductionOrders", migrations[^1]);
            var baseline = profile == "InventoryMigration" ? migrations.TakeWhile(x => !x.EndsWith("_AddInventoryLedger", StringComparison.Ordinal)).ToArray() : migrations;
            Assert.Equal(baseline, await db.Database.GetAppliedMigrationsAsync());
            if (profile == "InventoryMigration")
                Assert.Equal(migrations.Except(baseline), await db.Database.GetPendingMigrationsAsync());
            else Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            BootstrapSqlFixture.RequireEmpty(await IdentityDatabaseVerifier.CaptureDataAsync(db));
            Assert.False(await db.Categories.AnyAsync());
            Assert.False(await db.Items.AnyAsync());
            Assert.False(await db.Suppliers.AnyAsync());
            Assert.False(await db.Customers.AnyAsync());
            if (profile != "InventoryMigration")
            {
                Assert.False(await db.InventoryBalances.AnyAsync());
                Assert.False(await db.InventoryTransactions.AnyAsync());
                Assert.False(await db.PurchaseReceipts.AnyAsync());
                Assert.False(await db.PurchaseReceiptLines.AnyAsync());
                Assert.False(await db.ProductionOrders.AnyAsync());
                Assert.False(await db.ProductionOrderMaterials.AnyAsync());
                Assert.False(await db.BillOfMaterials.AnyAsync());
                Assert.False(await db.BillOfMaterialDetails.AnyAsync());
            }
            else
                Assert.Empty(await IdentityDatabaseVerifier.ReadRowsAsync(db,
                    "SELECT name FROM sys.tables WHERE name IN ('InventoryBalances','InventoryTransactions')"));
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
                diagnostics.Mark("cleanup-start", db);
                await using var transaction = await db.Database.BeginTransactionAsync();
                var items = ownership.Items.Keys.ToArray();
                var categories = ownership.Categories.Keys.ToArray();
                var suppliers = ownership.Suppliers.Keys.ToArray();
                var receipts = ownership.Receipts.Keys.ToArray();
                if (profile == "Production")
                {
                    var orders = ownership.Orders.Keys.ToArray();
                    var ownedOrders = db.ProductionOrders.Where(x => orders.Contains(x.Id) && x.CreatedByUserId == ActorId).Select(x => x.Id);
                    await db.ProductionOrderMaterials.Where(x => ownedOrders.Contains(x.ProductionOrderId)).ExecuteDeleteAsync();
                    await db.ProductionOrders.Where(x => orders.Contains(x.Id) && x.CreatedByUserId == ActorId).ExecuteDeleteAsync();
                }
                if (profile is "Bom" or "Production")
                {
                    var boms = ownership.Boms.Keys.ToArray();
                    var ownedBoms = db.BillOfMaterials.Where(x => boms.Contains(x.Id) && x.CreatedByUserId == ActorId).Select(x => x.Id);
                    await db.BillOfMaterialDetails.Where(x => ownedBoms.Contains(x.BillOfMaterialId)).ExecuteDeleteAsync();
                    await db.BillOfMaterials.Where(x => boms.Contains(x.Id) && x.CreatedByUserId == ActorId).ExecuteDeleteAsync();
                }
                if (profile is "Purchasing" or "Confirmation")
                {
                    var owned = db.PurchaseReceipts.Where(x => receipts.Contains(x.Id) && x.CreatedByUserId == ActorId).Select(x => x.Id);
                    await db.PurchaseReceiptLines.Where(x => owned.Contains(x.PurchaseReceiptId)).ExecuteDeleteAsync();
                    await db.PurchaseReceipts.Where(x => receipts.Contains(x.Id) && x.CreatedByUserId == ActorId).ExecuteDeleteAsync();
                }
                if (profile != "InventoryMigration")
                {
                    await db.InventoryTransactions.Where(x => items.Contains(x.ItemId) && x.PerformedByUserId == ActorId).ExecuteDeleteAsync();
                    await db.InventoryBalances.Where(x => items.Contains(x.ItemId) && x.CreatedByUserId == ActorId).ExecuteDeleteAsync();
                }
                await db.Items.Where(x => items.Contains(x.Id) && x.CreatedByUserId == ActorId).ExecuteDeleteAsync();
                await db.Categories.Where(x => categories.Contains(x.Id) && x.CreatedByUserId == ActorId).ExecuteDeleteAsync();
                await db.Suppliers.Where(x => suppliers.Contains(x.Id) && x.CreatedByUserId == ActorId).ExecuteDeleteAsync();
                await db.Users.Where(x => x.Id == ActorId).ExecuteDeleteAsync();
                await transaction.CommitAsync();
                diagnostics.Mark("cleanup-commit-completed", db);
                Assert.False(await db.Items.AnyAsync(x => x.CreatedByUserId == ActorId));
                Assert.False(await db.Categories.AnyAsync(x => x.CreatedByUserId == ActorId));
                Assert.False(await db.Users.AnyAsync(x => x.Id == ActorId));
                if (profile == "Production") Assert.False(await db.ProductionOrders.AnyAsync(x => x.CreatedByUserId == ActorId));
                if (profile is "Bom" or "Production") Assert.False(await db.BillOfMaterials.AnyAsync(x => x.CreatedByUserId == ActorId));
            }
        }
        finally
        {
            if (guard != null)
            {
                try
                {
                    if (ownsSessionLock && guard.Database.GetDbConnection().State == System.Data.ConnectionState.Open)
                    {
                        await guard.Database.ExecuteSqlRawAsync("EXEC sys.sp_releaseapplock @Resource=N'BttMiniERP.P21.Tests', @LockOwner=N'Session';");
                        ownsSessionLock = false;
                        if (mayCleanup && profile is "Inventory" or "InventoryMigration" or "Purchasing" or "Confirmation" or "Bom" or "Production")
                        {
                            Assert.Equal(0, await guard.Database.SqlQueryRaw<int>("SELECT @@TRANCOUNT AS [Value]").SingleAsync());
                            // SQL Server có transaction nền (đã quan sát session 2, is_user_process=0).
                            // Giữ mọi applock và transaction user/không xác định; chỉ loại session hệ thống đã xác minh.
                            diagnostics.Mark("lock-check-after-release", guard);
                            var snapshot = string.Concat(await IdentityDatabaseVerifier.ReadRowsAsync(guard, FixtureLockDiagnostics.SnapshotSql));
                            using var parsed = System.Text.Json.JsonDocument.Parse(snapshot);
                            Assert.True(parsed.RootElement.GetArrayLength() == 0, diagnostics.Describe(ActorId, snapshot));
                        }
                    }
                }
                finally { await guard.DisposeAsync(); }
            }
        }
    }

    private sealed class OwnedWrites : SaveChangesInterceptor
    {
        public ConcurrentDictionary<int, bool> Categories { get; } = new();
        public ConcurrentDictionary<int, bool> Items { get; } = new();
        public ConcurrentDictionary<int, bool> Suppliers { get; } = new();
        public ConcurrentDictionary<int, bool> Receipts { get; } = new();
        public ConcurrentDictionary<int, bool> Orders { get; } = new();
        public ConcurrentDictionary<int, bool> Boms { get; } = new();
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken cancellationToken = default)
        {
            foreach (var entry in data.Context!.ChangeTracker.Entries<Category>().Where(x => x.Entity.Id > 0)) Categories.TryAdd(entry.Entity.Id, true);
            foreach (var entry in data.Context.ChangeTracker.Entries<Item>().Where(x => x.Entity.Id > 0)) Items.TryAdd(entry.Entity.Id, true);
            foreach (var entry in data.Context.ChangeTracker.Entries<Supplier>().Where(x => x.Entity.Id > 0)) Suppliers.TryAdd(entry.Entity.Id, true);
            foreach (var entry in data.Context.ChangeTracker.Entries<PurchaseReceipt>().Where(x => x.Entity.Id > 0)) Receipts.TryAdd(entry.Entity.Id, true);
            foreach (var entry in data.Context.ChangeTracker.Entries<BillOfMaterial>().Where(x => x.Entity.Id > 0)) Boms.TryAdd(entry.Entity.Id, true);
            foreach (var entry in data.Context.ChangeTracker.Entries<ProductionOrder>().Where(x => x.Entity.Id > 0)) Orders.TryAdd(entry.Entity.Id, true);
            return ValueTask.FromResult(result);
        }
    }
}
