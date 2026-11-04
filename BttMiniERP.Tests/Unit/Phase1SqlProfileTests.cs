using BttMiniERP.Tests.Fixtures;

namespace BttMiniERP.Tests.Unit;

public sealed class Phase1SqlProfileTests
{
    private static Dictionary<string, string?> Settings() => new()
    {
        ["BTT_SQL_TEST_PROFILE"] = "Phase1", ["BTT_RUN_SQL_TESTS"] = "1",
        ["BTT_RUN_PHASE1_SQL_TESTS"] = "1", ["BTT_SQL_TEST_ENVIRONMENT"] = "Testing",
        ["BTT_SQL_APPROVED_SERVER"] = "localhost", ["BTT_SQL_VERIFICATION_STAGE"] = "After",
        ["BTT_SQL_VERIFICATION_RUN_ID"] = Guid.NewGuid().ToString("D"),
        ["BTT_PHASE1_SQL_APPROVED_OPERATIONS"] = "DownUp,Verify,TemporaryIdentityWrites,Concurrency,CleanupOwnedRecords",
        ["BTT_SQL_TEST_CONNECTION_STRING"] = "Server=localhost;Database=BttMiniERP_BootstrapTest;Integrated Security=True;Encrypt=True"
    };

    [Theory]
    [InlineData("Before")][InlineData("Down")][InlineData("After")]
    public void Phase1KeepsExactDatabaseAndExplicitStage(string stage)
    {
        var values = Settings();
        values["BTT_SQL_VERIFICATION_STAGE"] = stage;
        var settings = SqlServerTestSettings.Read(key => values.GetValueOrDefault(key));
        Assert.Equal(BootstrapSqlFixture.DatabaseName, settings.DatabaseName);
        Assert.Equal(stage, settings.Stage);
        Assert.True(settings.IsVerification); // ApplyMigrationsAsync chỉ kiểm tra schema, không Migrate.
    }

    [Theory]
    [InlineData("BTT_RUN_SQL_TESTS", "0")]
    [InlineData("BTT_RUN_PHASE1_SQL_TESTS", null)]
    [InlineData("BTT_SQL_TEST_ENVIRONMENT", "Development")]
    [InlineData("BTT_SQL_APPROVED_SERVER", "other")]
    [InlineData("BTT_SQL_VERIFICATION_STAGE", null)]
    [InlineData("BTT_SQL_VERIFICATION_STAGE", "Unknown")]
    [InlineData("BTT_SQL_VERIFICATION_RUN_ID", null)]
    [InlineData("BTT_PHASE1_SQL_APPROVED_OPERATIONS", null)]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", null)]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_Dev;Integrated Security=True")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_Test;Integrated Security=True")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=other;Database=BttMiniERP_BootstrapTest;Integrated Security=True")]
    public void Phase1FailsClosed(string key, string? value)
    {
        var values = Settings();
        values[key] = value;
        Assert.Throws<InvalidOperationException>(() => SqlServerTestSettings.Read(name => values.GetValueOrDefault(name)));
    }
}
