using BttMiniERP.Tests.Fixtures;

namespace BttMiniERP.Tests.Unit.Partners;

public sealed class PartnersSqlGuardTests
{
    [Theory]
    [InlineData("BTT_RUN_SQL_TESTS", "0")][InlineData("BTT_RUN_PARTNERS_SQL_TESTS", "0")]
    [InlineData("BTT_SQL_TEST_PROFILE", "Phase1")][InlineData("BTT_SQL_TEST_ENVIRONMENT", "Development")]
    [InlineData("BTT_SQL_APPROVED_SERVER", "other")][InlineData("BTT_PARTNERS_SQL_APPROVED_OPERATIONS", "")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_Dev;Integrated Security=True")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_Test;Integrated Security=True")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_BootstrapTest;Integrated Security=True")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=other;Database=BttMiniERP_PartnersTest;Integrated Security=True")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_PartnersTest;User Id=sa;Password=synthetic-only")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_PartnersTest;Integrated Security=True;AttachDbFilename=x.mdf")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_PartnersTest;Integrated Security=True;Failover Partner=other")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_PartnersTest;Integrated Security=True;Encrypt=False")]
    public void RefusesUnsafeTargetsAndMissingApprovalWithoutConnecting(string key, string value)
    {
        var settings = Settings();
        settings[key] = value;
        var error = Assert.Throws<InvalidOperationException>(() => PartnersSqlFixture.ReadSettings(settings.GetValueOrDefault));
        Assert.DoesNotContain("synthetic-only", error.ToString());
    }

    [Fact]
    public void AcceptsOnlyDedicatedBusinessProfileWithoutConnecting()
        => Assert.Contains(PartnersSqlFixture.DatabaseName, PartnersSqlFixture.ReadSettings(Settings().GetValueOrDefault));

    private static Dictionary<string, string?> Settings() => new()
    {
        ["BTT_RUN_SQL_TESTS"] = "1", ["BTT_RUN_PARTNERS_SQL_TESTS"] = "1",
        ["BTT_SQL_TEST_PROFILE"] = "Partners", ["BTT_SQL_TEST_ENVIRONMENT"] = "Testing",
        ["BTT_SQL_APPROVED_SERVER"] = "localhost",
        ["BTT_PARTNERS_SQL_APPROVED_OPERATIONS"] = "Verify,TemporaryWrites,Concurrency,CleanupOwnedRecords",
        ["BTT_SQL_TEST_CONNECTION_STRING"] = "Server=localhost;Database=BttMiniERP_PartnersTest;Integrated Security=True;Encrypt=True"
    };
}
