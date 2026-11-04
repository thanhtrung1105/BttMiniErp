using BttMiniERP.Api.Data;
using BttMiniERP.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;

namespace BttMiniERP.Tests.Unit.Auth;

public sealed class BootstrapSafetyTests
{
    private static Dictionary<string, string?> Settings() => new()
    {
        ["BTT_RUN_SQL_TESTS"] = "1", ["BTT_RUN_BOOTSTRAP_SQL_TESTS"] = "1",
        ["BTT_SQL_TEST_ENVIRONMENT"] = "Testing", ["BTT_SQL_TEST_PROFILE"] = "Bootstrap",
        ["BTT_SQL_VERIFICATION_STAGE"] = "After", ["BTT_SQL_APPROVED_SERVER"] = "localhost",
        ["BTT_BOOTSTRAP_SQL_APPROVED_OPERATIONS"] = "Bootstrap,TemporaryIdentityWrites,CleanupOwnedRecords",
        ["BTT_SQL_TEST_CONNECTION_STRING"] = "Server=localhost;Database=BttMiniERP_BootstrapTest;Integrated Security=True;Encrypt=True"
    };

    [Fact]
    public void BootstrapSqlGuardAcceptsOnlyExplicitOptInWithoutOpeningConnection()
    {
        var values = Settings();
        Assert.Equal("BttMiniERP_BootstrapTest", BootstrapSqlFixture.ReadSettings(key => values.GetValueOrDefault(key)).DatabaseName);
    }

    [Theory]
    [InlineData("BTT_RUN_SQL_TESTS")]
    [InlineData("BTT_RUN_BOOTSTRAP_SQL_TESTS")]
    [InlineData("BTT_SQL_TEST_ENVIRONMENT")]
    [InlineData("BTT_SQL_TEST_PROFILE")]
    [InlineData("BTT_SQL_VERIFICATION_STAGE")]
    [InlineData("BTT_SQL_APPROVED_SERVER")]
    [InlineData("BTT_BOOTSTRAP_SQL_APPROVED_OPERATIONS")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING")]
    public void BootstrapProfileFailsClosedWhenRequiredSettingIsMissing(string key)
    {
        var values = Settings();
        values.Remove(key);
        Assert.Throws<InvalidOperationException>(() => BootstrapSqlFixture.ReadSettings(name => values.GetValueOrDefault(name)));
    }

    [Theory]
    [InlineData("BTT_RUN_SQL_TESTS", "0")]
    [InlineData("BTT_RUN_BOOTSTRAP_SQL_TESTS", "0")]
    [InlineData("BTT_BOOTSTRAP_SQL_APPROVED_OPERATIONS", "")]
    [InlineData("BTT_SQL_TEST_PROFILE", "Default")]
    [InlineData("BTT_SQL_TEST_ENVIRONMENT", "Development")]
    [InlineData("BTT_SQL_TEST_PROFILE", "MigrationUpCheck")]
    [InlineData("BTT_SQL_VERIFICATION_STAGE", "Before")]
    [InlineData("BTT_SQL_APPROVED_SERVER", "other")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_Dev;Integrated Security=True")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_Test_copy;Integrated Security=True")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_Test;User Id=test;Password=synthetic-only")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_Test;Integrated Security=True;AttachDbFilename=other.mdf")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_Test;Integrated Security=True")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=other;Database=BttMiniERP_BootstrapTest;Integrated Security=True")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_BootstrapTest;Integrated Security=True;Failover Partner=other")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_BootstrapTest;Integrated Security=True;User Instance=True")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_BootstrapTest;User Id=test;Password=synthetic-only")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_BootstrapTest;Integrated Security=True;AttachDbFilename=other.mdf")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "not a connection string")]
    public void BootstrapSqlGuardRejectsUnsafeSettingsBeforeConnection(string key, string value)
    {
        var values = Settings();
        values[key] = value;
        var error = Assert.Throws<InvalidOperationException>(() => BootstrapSqlFixture.ReadSettings(name => values.GetValueOrDefault(name)));
        Assert.DoesNotContain("synthetic-only", error.ToString());
    }

    [Theory]
    [InlineData("AspNetUsers")][InlineData("AspNetRoles")][InlineData("AspNetUserRoles")]
    [InlineData("AspNetUserClaims")][InlineData("AspNetRoleClaims")][InlineData("AspNetUserLogins")][InlineData("AspNetUserTokens")]
    public void BootstrapFixtureRefusesAnyExistingIdentityData(string occupiedTable)
    {
        var tables = EmptyTables().Select(table => table.Table == occupiedTable ? table with { Rows = 1 } : table);
        Assert.Throws<InvalidOperationException>(() => BootstrapSqlFixture.RequireEmpty(tables));
    }

    [Fact]
    public void EmptyBaselineGuardRequiresAllSevenTables()
    {
        BootstrapSqlFixture.RequireEmpty(EmptyTables());
        Assert.Throws<InvalidOperationException>(() => BootstrapSqlFixture.RequireEmpty([]));
        Assert.Throws<InvalidOperationException>(() => BootstrapSqlFixture.RequireEmpty(EmptyTables().Skip(1)));
        Assert.Throws<InvalidOperationException>(() => BootstrapSqlFixture.RequireEmpty(EmptyTables().Append(EmptyTables()[0])));
    }

    [Theory]
    [InlineData("localhost", "BttMiniERP_Dev")]
    [InlineData("other", "BttMiniERP_Test")]
    public void BootstrapCliRejectsMismatchedTestingTarget(string server, string database)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=BttMiniERP_Test;Integrated Security=True" }).Build();
        Assert.Throws<InvalidOperationException>(() => IdentitySeeder.ValidateTarget(config, "Testing",
            ["--seed-identity=true", "--approved-server=" + server, "--approved-database=" + database]));
        IdentitySeeder.ValidateTarget(config, "Testing",
            ["--seed-identity=true", "--approved-server=localhost", "--approved-database=BttMiniERP_Test"]);
    }

    [Fact]
    public void PendingTestCatalogKeepsRealMigrationsAndCannotExecuteThem()
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=BttMiniERP_Test;Integrated Security=True;Connect Timeout=1");
        using var original = new ApplicationDbContext(builder.Options);
        var real = original.Database.GetMigrations().ToArray(); // chỉ đọc assembly, không SQL
        builder.ReplaceService<IMigrationsAssembly, BootstrapPendingMigrationsAssembly>();
        using var pending = new ApplicationDbContext(builder.Options);
        Assert.Equal(real.Append(BootstrapPendingMigrationsAssembly.PendingId), pending.Database.GetMigrations());
        var catalog = pending.GetService<IMigrationsAssembly>();
        Assert.Throws<InvalidOperationException>(() => catalog.CreateMigration(catalog.Migrations.Values.First(), "Microsoft.EntityFrameworkCore.SqlServer"));
        Assert.DoesNotContain(BootstrapPendingMigrationsAssembly.PendingId, original.Database.GetMigrations());
    }

    private static IdentityDatabaseVerifier.TableFingerprint[] EmptyTables()
        => new[] { "AspNetUsers", "AspNetRoles", "AspNetUserRoles", "AspNetUserClaims", "AspNetRoleClaims", "AspNetUserLogins", "AspNetUserTokens" }
            .Select(name => new IdentityDatabaseVerifier.TableFingerprint(name, 0, "unused-in-guard-test")).ToArray();
}
