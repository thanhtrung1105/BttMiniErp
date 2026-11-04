using BttMiniERP.Api.Data;
using Microsoft.Data.SqlClient;

namespace BttMiniERP.Tests.Fixtures;

public sealed class SqlServerTestSettings
{
    public required string ConnectionString { get; init; }
    public string Profile { get; init; } = "Default";
    public string DatabaseName { get; init; } = "BttMiniERP_Test";
    public string Stage { get; init; } = "After";
    public string? VerificationRunId { get; init; }
    public bool IsVerification => Profile != "Default";

    public static SqlServerTestSettings Read(Func<string, string?> read)
    {
        var profile = read("BTT_SQL_TEST_PROFILE") ?? "Default";
        if (profile == "Phase1")
        {
            var phaseStage = read("BTT_SQL_VERIFICATION_STAGE");
            var phaseRun = read("BTT_SQL_VERIFICATION_RUN_ID");
            if (read("BTT_RUN_SQL_TESTS") != "1" || read("BTT_RUN_PHASE1_SQL_TESTS") != "1"
                || read("BTT_SQL_TEST_ENVIRONMENT") != "Testing"
                || read("BTT_SQL_APPROVED_SERVER") != "localhost"
                || read("BTT_PHASE1_SQL_APPROVED_OPERATIONS") != "DownUp,Verify,TemporaryIdentityWrites,Concurrency,CleanupOwnedRecords"
                || phaseStage is not ("Before" or "Down" or "After") || !Guid.TryParseExact(phaseRun, "D", out _))
                throw new InvalidOperationException("Phase1 cần opt-in, phạm vi thao tác, stage và run ID hợp lệ.");
            var phaseConnection = read("BTT_SQL_TEST_CONNECTION_STRING");
            BootstrapSqlFixture.ValidateConnection(phaseConnection);
            return new SqlServerTestSettings { ConnectionString = phaseConnection!, Profile = "Phase1",
                DatabaseName = BootstrapSqlFixture.DatabaseName, Stage = phaseStage, VerificationRunId = phaseRun };
        }
        var database = profile switch
        {
            "Default" => "BttMiniERP_Test",
            "MigrationUpCheck" => "BttMiniERP_MigrationUpCheck",
            "MigrationDownCheck" => "BttMiniERP_MigrationDownCheck",
            _ => throw new InvalidOperationException("Profile SQL test không được phép.")
        };
        var verification = profile != "Default";
        if (read("BTT_RUN_SQL_TESTS") != "1"
            || read("BTT_SQL_TEST_ENVIRONMENT") != "Testing"
            || read("BTT_SQL_APPROVED_OPERATIONS") != (verification ? "Verify,RoundTripRollback" : "Migrate,RoundTripRollback"))
            throw new InvalidOperationException("Chưa xác nhận môi trường và phạm vi thao tác SQL test.");

        var raw = read("BTT_SQL_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException("Thiếu cấu hình SQL test.");
        Microsoft.Data.SqlClient.SqlConnectionStringBuilder parsed;
        try { parsed = new(raw); }
        catch (ArgumentException) { throw new InvalidOperationException("Cấu hình SQL test không hợp lệ."); }
        if (!string.Equals(parsed.InitialCatalog, database, StringComparison.Ordinal))
            throw new InvalidOperationException("Database không khớp profile SQL test.");
        // Tái sử dụng toàn bộ guard attach/user-instance/failover; không nới cấu hình ứng dụng.
        parsed.InitialCatalog = "BttMiniERP_Test";
        DatabaseConnectionSettings.Validate(parsed.ConnectionString, "Testing");
        parsed.InitialCatalog = database;
        var connection = parsed.ConnectionString;
        var server = new SqlConnectionStringBuilder(connection).DataSource;
        if (string.IsNullOrWhiteSpace(read("BTT_SQL_APPROVED_SERVER"))
            || !string.Equals(server, read("BTT_SQL_APPROVED_SERVER"), StringComparison.Ordinal))
            throw new InvalidOperationException("SQL instance không khớp đích đã được duyệt.");
        var stage = read("BTT_SQL_VERIFICATION_STAGE") ?? "After";
        var runId = read("BTT_SQL_VERIFICATION_RUN_ID");
        if (verification && (server != "localhost" || !parsed.IntegratedSecurity
            || !string.IsNullOrEmpty(parsed.UserID) || !string.IsNullOrEmpty(parsed.Password)
            || stage is not ("Before" or "After") || !Guid.TryParseExact(runId, "D", out _)))
            throw new InvalidOperationException("Verification cần localhost/Windows, stage Before hoặc After và run ID GUID.");
        return new SqlServerTestSettings { ConnectionString = connection, Profile = profile,
            DatabaseName = database, Stage = stage, VerificationRunId = runId };
    }
}
