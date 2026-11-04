using System.Text.Json;
using BttMiniERP.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Tests.Integration;

[Collection("SQL Server")]
[Trait("Category", "SqlServer")]
[Trait("SqlSuite", "MigrationSnapshot")]
public sealed class MigrationDataSnapshotTests(SqlServerFixture fixture)
{
    public sealed record Evidence(string Profile, string Database, string Server, string RunId,
        IdentityDatabaseVerifier.TableFingerprint[] Tables);

    [SqlServerFact]
    public async Task CaptureBeforeOrCompareAfterMigrationWithoutChangingData()
    {
        fixture.RequireVerification();
        await using var context = fixture.CreateContext();
        await fixture.ApplyMigrationsAsync(context); // verification profile chỉ đọc schema/history
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var tables = await IdentityDatabaseVerifier.CaptureDataAsync(context);
        await transaction.RollbackAsync();
        var settings = fixture.Settings;
        var evidence = new Evidence(settings.Profile, settings.DatabaseName, "localhost", settings.VerificationRunId!, tables);
        var folder = Path.Combine(AppContext.BaseDirectory, "TestResults", "MigrationVerification", settings.VerificationRunId!);
        var path = Path.Combine(folder, settings.Profile + ".before.json");
        if (settings.Stage == "Before")
        {
            // Không cho dữ liệu rỗng tạo bằng chứng bảo toàn dữ liệu giả.
            foreach (var table in new[] { "AspNetUsers", "AspNetRoles", "AspNetUserRoles", "AspNetUserLogins", "AspNetUserTokens" })
                Assert.True(tables.Single(t => t.Table == table).Rows > 0,
                    "Cần bộ dữ liệu mẫu được duyệt trước snapshot; test không tự seed.");
            Directory.CreateDirectory(folder);
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            await JsonSerializer.SerializeAsync(file, evidence);
        }
        else
        {
            Assert.True(File.Exists(path), "Thiếu snapshot Before của đúng profile/run ID.");
            var baseline = JsonSerializer.Deserialize<Evidence>(await File.ReadAllTextAsync(path))!;
            Assert.Equal(evidence.Profile, baseline.Profile);
            Assert.Equal(evidence.Database, baseline.Database);
            Assert.Equal(evidence.Server, baseline.Server);
            Assert.Equal(evidence.RunId, baseline.RunId);
            Assert.Equal(baseline.Tables, evidence.Tables);
        }
    }
}
