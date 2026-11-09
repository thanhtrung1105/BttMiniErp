using BttMiniERP.Api.Data;
using BttMiniERP.Api.Entities;
using BttMiniERP.Tests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BttMiniERP.Tests.Unit;

public sealed class PersistenceConfigurationTests
{
    [Fact]
    public void MigrationMatchesModelAndGeneratesSqlOffline()
    {
        using var context = new ApplicationDbContextFactory().CreateDbContext(["--offline"]);
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(4, context.Database.GetMigrations().Count());
        var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(context);
        var script = migrator.GenerateScript(options: Microsoft.EntityFrameworkCore.Migrations.MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("CREATE TABLE [AspNetUsers]", script);
        Assert.Contains("CREATE TABLE [AspNetUserRoles]", script);
        Assert.DoesNotContain("INSERT INTO [AspNetUsers]", script);
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public void RegistersScopedSqlServerContextWithoutOpeningConnection()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=127.0.0.1,1;Database=BttMiniERP_Dev;Integrated Security=True"
        }).Build();
        var services = new ServiceCollection();
        services.AddPersistence(configuration, "Development");
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var context = first.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Same(context, first.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        Assert.NotSame(context, second.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", context.Database.ProviderName);
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public void IdentityModelHasStringKeysUniqueNamesConcurrencyAndNoCascade()
    {
        using var context = new ApplicationDbContextFactory().CreateDbContext(["--offline"]);
        var user = context.Model.FindEntityType(typeof(ApplicationUser))!;
        Assert.Equal("AspNetUsers", user.GetTableName());
        Assert.Equal(typeof(string), user.FindPrimaryKey()!.Properties.Single().ClrType);
        Assert.Equal(256, user.FindProperty("NormalizedUserName")!.GetMaxLength());
        Assert.True(user.FindProperty("ConcurrencyStamp")!.IsConcurrencyToken);
        Assert.Contains(user.GetIndexes(), index => index.IsUnique && index.Properties.Single().Name == "NormalizedUserName");
        Assert.NotNull(context.Model.FindEntityType(typeof(IdentityUserRole<string>)));
        Assert.All(context.Model.GetEntityTypes().SelectMany(entity => entity.GetForeignKeys()),
            foreignKey => Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior));
    }

    [Theory]
    [InlineData("BttMiniERP_Dev", "Testing")]
    [InlineData("BttMiniERP_Test", "Development")]
    [InlineData("master", "Testing")]
    [InlineData("BttMiniERP_Test_copy", "Testing")]
    [InlineData("BttMiniERP_Dev", "Production")]
    public void RejectsWrongDatabaseOrEnvironment(string database, string environment)
        => Assert.Throws<InvalidOperationException>(() => DatabaseConnectionSettings.Validate($"Server=test;Database={database}", environment));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Password=synthetic-secret;invalid-key=value")]
    [InlineData("Server=test;Database=BttMiniERP_Test;AttachDbFilename=test.mdf")]
    [InlineData("Server=test;Database=BttMiniERP_Test;User Instance=True")]
    [InlineData("Server=test;Database=BttMiniERP_Test;Failover Partner=other")]
    public void RejectsUnsafeConfigurationWithoutLeakingSecrets(string? connection)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => DatabaseConnectionSettings.Validate(connection, "Testing"));
        Assert.DoesNotContain("synthetic-secret", exception.ToString());
    }

    [Theory]
    [InlineData("BTT_RUN_SQL_TESTS", "0")]
    [InlineData("BTT_SQL_TEST_ENVIRONMENT", "Development")]
    [InlineData("BTT_SQL_APPROVED_SERVER", "other")]
    [InlineData("BTT_SQL_APPROVED_SERVER", "")]
    [InlineData("BTT_SQL_APPROVED_OPERATIONS", "")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=test;Database=BttMiniERP_Dev")]
    public void TestGuardFailsClosed(string key, string value)
    {
        var settings = ApprovedSettings();
        settings[key] = value;
        Assert.Throws<InvalidOperationException>(() => SqlServerTestSettings.Read(name => settings.GetValueOrDefault(name)));
    }

    [Fact]
    public void TestGuardAcceptsOnlyExplicitApprovedTarget()
    {
        var settings = ApprovedSettings();
        Assert.Contains("BttMiniERP_Test", SqlServerTestSettings.Read(name => settings.GetValueOrDefault(name)).ConnectionString);
    }

    private static Dictionary<string, string?> ApprovedSettings() => new()
    {
        ["BTT_RUN_SQL_TESTS"] = "1", ["BTT_SQL_TEST_ENVIRONMENT"] = "Testing",
        ["BTT_SQL_APPROVED_SERVER"] = "test", ["BTT_SQL_APPROVED_OPERATIONS"] = "Migrate,RoundTripRollback",
        ["BTT_SQL_TEST_CONNECTION_STRING"] = "Server=test;Database=BttMiniERP_Test;Integrated Security=True"
    };
}
