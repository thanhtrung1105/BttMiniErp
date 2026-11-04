using BttMiniERP.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Tests.Fixtures;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BTT_RUN_SQL_TESTS") != "1")
            Skip = "SQL Server chưa được cấp phép; cần opt-in và các guard hợp lệ.";
    }
}

[CollectionDefinition("SQL Server", DisableParallelization = true)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private SqlServerTestSettings? settings;
    public SqlServerTestSettings Settings => settings ?? throw new InvalidOperationException("SQL test chưa được bật.");

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("BTT_RUN_SQL_TESTS") == "1")
            settings = SqlServerTestSettings.Read(Environment.GetEnvironmentVariable);
        if (settings?.Profile == "Phase1")
        {
            await using var context = CreateContext();
            await VerifyPhase1TargetAsync(context);
            await IdentityDatabaseVerifier.AssertSchemaAsync(context, ExpectedLength);
        }
    }

    internal static async Task VerifyPhase1TargetAsync(ApplicationDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        var rows = await IdentityDatabaseVerifier.ReadRowsAsync(context,
            "SELECT DB_NAME(), CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')), COALESCE(CONVERT(nvarchar(128),SERVERPROPERTY('InstanceName')),N''), CONVERT(nvarchar(30),CONNECTIONPROPERTY('auth_scheme'))");
        Assert.True(rows.Count == 1 && new[] { "NTLM", "KERBEROS" }.Any(auth =>
            rows[0] == $"{BootstrapSqlFixture.DatabaseName}|{Environment.MachineName}||{auth}"), "Sai database/default instance/Windows Authentication.");
    }

    public ApplicationDbContext CreateContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlServer((settings ?? throw new InvalidOperationException("SQL test chưa được bật.")).ConnectionString).Options);

    public async Task ApplyMigrationsAsync(ApplicationDbContext context)
    {
        // Mở database có sẵn trước: không để Migrate tự tạo database bị thiếu.
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT DB_NAME()";
        if (!string.Equals(await command.ExecuteScalarAsync() as string, Settings.DatabaseName, StringComparison.Ordinal))
            throw new InvalidOperationException("Database thực tế không phải BttMiniERP_Test.");
        if (Settings.IsVerification)
        {
            await IdentityDatabaseVerifier.AssertSchemaAsync(context, ExpectedLength);
            return; // Hai database kiểm chứng KHÔNG BAO GIỜ gọi Migrate.
        }
        // Opt-in cũ không tự cấp quyền áp dụng migration mới.
        var pending = await context.Database.GetPendingMigrationsAsync();
        ValidateApprovedMigrations(pending, Environment.GetEnvironmentVariable("BTT_SQL_APPROVED_MIGRATION"));
        await context.Database.MigrateAsync();
    }

    public int ExpectedLength => Settings.Profile switch
    {
        "MigrationUpCheck" => Settings.Stage == "Before" ? 450 : 128,
        "MigrationDownCheck" => Settings.Stage == "Before" ? 128 : 450,
        "Phase1" => Settings.Stage == "Down" ? 450 : 128,
        _ => 128
    };

    public async Task PreparePersistenceAsync(ApplicationDbContext context)
    {
        if (Settings.Profile == "Phase1")
        {
            if (Settings.Stage != "After") throw new InvalidOperationException("Persistence Phase1 chỉ chạy After.");
        }
        else if (Settings.IsVerification) RequireVerification("MigrationUpCheck", "After");
        await ApplyMigrationsAsync(context);
    }

    public async Task PrepareConstraintChecksAsync(ApplicationDbContext context, bool require128)
    {
        ValidateConstraintProfile(Settings, require128);
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT DB_NAME()";
        Assert.Equal(Settings.DatabaseName, await command.ExecuteScalarAsync());
        // Chỉ xác minh schema/history có sẵn; không tự migrate trong suite constraint.
        await IdentityDatabaseVerifier.AssertSchemaAsync(context, ExpectedLength);
    }

    public static void ValidateConstraintProfile(SqlServerTestSettings settings, bool require128)
    {
        var allowed = settings.Profile switch
        {
            "Default" => settings.DatabaseName == "BttMiniERP_Test" && settings.Stage == "After",
            "MigrationUpCheck" => settings.DatabaseName == "BttMiniERP_MigrationUpCheck" && settings.Stage == "After",
            "MigrationDownCheck" => !require128 && settings.DatabaseName == "BttMiniERP_MigrationDownCheck" && settings.Stage == "After",
            "Phase1" => settings.DatabaseName == BootstrapSqlFixture.DatabaseName
                && (settings.Stage == "After" || (!require128 && settings.Stage == "Down")),
            _ => false
        };
        var target = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(settings.ConnectionString);
        if (!allowed || target.DataSource != "localhost" || target.InitialCatalog != settings.DatabaseName
            || !target.IntegratedSecurity || !string.IsNullOrEmpty(target.UserID) || !string.IsNullOrEmpty(target.Password)
            || !string.IsNullOrEmpty(target.AttachDBFilename) || target.UserInstance || !string.IsNullOrEmpty(target.FailoverPartner))
            throw new InvalidOperationException("Constraint suite cần đúng profile/After, database, localhost và Windows Authentication.");
    }

    public void RequireVerification(string? profile = null, string? stage = null)
    {
        if (Settings.Profile == "Phase1" && profile == "MigrationDownCheck" && stage == "After")
        {
            if (Settings.Stage != "Down") throw new InvalidOperationException("Down suite cần stage Down.");
            return;
        }
        if (!Settings.IsVerification || (profile != null && Settings.Profile != profile)
            || (stage != null && Settings.Stage != stage))
            throw new InvalidOperationException("Suite/stage không khớp profile kiểm chứng.");
    }

    public static void ValidateApprovedMigrations(IEnumerable<string> pending, string? approvedMigration)
    {
        var approved = approvedMigration ?? "20261103102340_CreateIdentitySchema";
        if (pending.Any(migration => !string.Equals(migration, approved, StringComparison.Ordinal)))
            throw new InvalidOperationException("Có migration pending ngoài phạm vi được duyệt; dừng trước thao tác ghi SQL.");
    }

    // Không drop/reset database hoặc xóa dữ liệu của người dùng.
    public Task DisposeAsync() => Task.CompletedTask;
}
