using BttMiniERP.Tests.Fixtures;

namespace BttMiniERP.Tests.Unit;

public sealed class MigrationVerificationGuardTests
{
    private static Dictionary<string, string?> Settings(string profile = "MigrationUpCheck") => new()
    {
        ["BTT_RUN_SQL_TESTS"] = "1", ["BTT_SQL_TEST_ENVIRONMENT"] = "Testing",
        ["BTT_SQL_APPROVED_SERVER"] = "localhost", ["BTT_SQL_APPROVED_OPERATIONS"] = "Verify,RoundTripRollback",
        ["BTT_SQL_TEST_PROFILE"] = profile, ["BTT_SQL_VERIFICATION_STAGE"] = "Before",
        ["BTT_SQL_VERIFICATION_RUN_ID"] = Guid.NewGuid().ToString(),
        ["BTT_SQL_TEST_CONNECTION_STRING"] = $"Server=localhost;Database=BttMiniERP_{profile};Integrated Security=True;Encrypt=True"
    };

    [Theory]
    [InlineData("MigrationUpCheck")]
    [InlineData("MigrationDownCheck")]
    public void ProfilesSelectOnlyTheirExactDatabase(string profile)
    {
        var values = Settings(profile);
        var result = SqlServerTestSettings.Read(key => values.GetValueOrDefault(key));
        Assert.True(result.IsVerification);
        Assert.Equal("BttMiniERP_" + profile, result.DatabaseName);
        Assert.Equal("Before", result.Stage);
    }

    [Theory]
    [InlineData("BTT_SQL_TEST_PROFILE", "MigrationOther")]
    [InlineData("BTT_SQL_VERIFICATION_STAGE", "Anything")]
    [InlineData("BTT_SQL_VERIFICATION_RUN_ID", "../escape")]
    [InlineData("BTT_SQL_VERIFICATION_RUN_ID", "")]
    [InlineData("BTT_SQL_APPROVED_OPERATIONS", "Migrate,RoundTripRollback")]
    [InlineData("BTT_SQL_APPROVED_SERVER", "other")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_Dev;Integrated Security=True")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_Test;Integrated Security=True")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_MigrationDownCheck;Integrated Security=True")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_MigrationUpCheck_copy;Integrated Security=True")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_MigrationUpCheck;User Id=test;Password=synthetic-secret")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_MigrationUpCheck;Integrated Security=True;AttachDbFilename=test.mdf")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_MigrationUpCheck;Integrated Security=True;User Instance=True")]
    [InlineData("BTT_SQL_TEST_CONNECTION_STRING", "Server=localhost;Database=BttMiniERP_MigrationUpCheck;Integrated Security=True;Failover Partner=other")]
    public void VerificationGuardsFailClosed(string key, string value)
    {
        var values = Settings();
        values[key] = value;
        var error = Assert.Throws<InvalidOperationException>(() => SqlServerTestSettings.Read(name => values.GetValueOrDefault(name)));
        Assert.DoesNotContain("synthetic-secret", error.ToString());
    }

    [Fact]
    public void FingerprintDetectsModificationDeletionAndAmbiguousConcatenation()
    {
        var first = IdentityDatabaseVerifier.Fingerprint("Users", new[] { "ab", "c" });
        Assert.Equal(first, IdentityDatabaseVerifier.Fingerprint("Users", new[] { "ab", "c" }));
        Assert.NotEqual(first.Sha256, IdentityDatabaseVerifier.Fingerprint("Users", new[] { "a", "bc" }).Sha256);
        Assert.NotEqual(first.Sha256, IdentityDatabaseVerifier.Fingerprint("Users", new[] { "ab" }).Sha256);
        Assert.Equal(2, first.Rows);
    }

    [Theory]
    [InlineData("Default", "BttMiniERP_Test", "After", true, true)]
    [InlineData("Default", "BttMiniERP_Dev", "After", true, false)]
    [InlineData("Default", "BttMiniERP_Test", "Before", true, false)]
    [InlineData("MigrationUpCheck", "BttMiniERP_MigrationUpCheck", "After", true, true)]
    [InlineData("MigrationUpCheck", "BttMiniERP_Test", "After", true, false)]
    [InlineData("MigrationDownCheck", "BttMiniERP_MigrationDownCheck", "After", true, false)]
    [InlineData("MigrationDownCheck", "BttMiniERP_MigrationDownCheck", "After", false, true)]
    public void ConstraintSuitesKeepExactDatabaseAndSchemaStageGuards(string profile, string database, string stage, bool require128, bool allowed)
    {
        var settings = new SqlServerTestSettings { Profile = profile, DatabaseName = database, Stage = stage,
            ConnectionString = $"Server=localhost;Database={database};Integrated Security=True" };
        if (allowed) SqlServerFixture.ValidateConstraintProfile(settings, require128);
        else Assert.Throws<InvalidOperationException>(() => SqlServerFixture.ValidateConstraintProfile(settings, require128));
    }

    [Theory]
    [InlineData("NormalizedName")]
    [InlineData("NormalizedUserName")]
    public void OnlyKnownEquivalentIndexFiltersAreNormalized(string column)
    {
        var expected = $"[{column}] IS NOT NULL";
        Assert.Equal(expected, IdentityDatabaseVerifier.NormalizeIndexFilter(expected));
        Assert.Equal(expected, IdentityDatabaseVerifier.NormalizeIndexFilter($"({expected})"));
        Assert.NotEqual(expected, IdentityDatabaseVerifier.NormalizeIndexFilter($"([{column}] IS NULL)"));
        Assert.NotEqual(expected, IdentityDatabaseVerifier.NormalizeIndexFilter($"({expected} AND [{column}] <> N'Admin')"));
        Assert.NotEqual(expected, IdentityDatabaseVerifier.NormalizeIndexFilter(null));
    }
}
