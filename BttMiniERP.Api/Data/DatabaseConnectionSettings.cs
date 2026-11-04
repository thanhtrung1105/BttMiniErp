using Microsoft.Data.SqlClient;

namespace BttMiniERP.Api.Data;

public static class DatabaseConnectionSettings
{
    public static string Validate(string? connectionString, string environment)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Thiếu ConnectionStrings:DefaultConnection. Cấu hình qua User Secrets hoặc biến môi trường.");

        SqlConnectionStringBuilder settings;
        try { settings = new SqlConnectionStringBuilder(connectionString); }
        catch (ArgumentException) { throw new InvalidOperationException("Connection string SQL Server không hợp lệ; không ghi nội dung cấu hình vào log."); }

        var expectedDatabase = environment switch
        {
            "Development" => "BttMiniERP_Dev",
            "Testing" => "BttMiniERP_Test",
            _ => throw new InvalidOperationException("P1.3 chỉ cho phép persistence trong Development hoặc Testing.")
        };
        if (string.IsNullOrWhiteSpace(settings.DataSource)
            || !string.Equals(settings.InitialCatalog, expectedDatabase, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(settings.AttachDBFilename)
            || settings.UserInstance
            || !string.IsNullOrEmpty(settings.FailoverPartner))
            throw new InvalidOperationException("Đích SQL không hợp lệ: cần server cụ thể và đúng database của môi trường; không attach, user instance hoặc failover partner.");

        return settings.ConnectionString;
    }
}
