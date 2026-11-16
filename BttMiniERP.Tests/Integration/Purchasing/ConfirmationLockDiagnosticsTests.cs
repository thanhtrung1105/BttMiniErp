using System.Text.Json;
using BttMiniERP.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Tests.Integration.Purchasing;

[Collection("MasterData SQL"), Trait("Category", "SqlServer"), Trait("SqlSuite", "PurchaseConfirmationDiagnostics")]
public sealed class ConfirmationLockDiagnosticsTests
{
    [ConfirmationSqlFact]
    public async Task SnapshotDetectsIndependentTransactionAndRetainsSessionIdentity()
    {
        await using var fixture = await MasterDataSqlFixture.OpenAsync("Confirmation");
        await using var observer = fixture.Context();
        await observer.Database.OpenConnectionAsync();
        await using var holder = fixture.Context();
        await holder.Database.OpenConnectionAsync();
        var connection = (SqlConnection)holder.Database.GetDbConnection();
        var spid = connection.ServerProcessId;
        // ClientConnectionId không giả định bằng connection_id của DMV (đặc biệt Shared Memory).
        var connectionId = await holder.Database.SqlQueryRaw<Guid>(
            "SELECT connection_id AS [Value] FROM sys.dm_exec_connections WHERE session_id=@@SPID").SingleAsync();
        await using (var transaction = await holder.Database.BeginTransactionAsync())
        {
            // Chỉ đọc/giữ khóa bảng rỗng để kiểm chứng detector, không thay dữ liệu.
            await holder.Database.ExecuteSqlRawAsync("SELECT COUNT_BIG(*) FROM [Categories] WITH (TABLOCKX, HOLDLOCK)");
            var json = string.Concat(await IdentityDatabaseVerifier.ReadRowsAsync(observer, FixtureLockDiagnostics.SnapshotSql));
            using var snapshot = JsonDocument.Parse(json);
            Assert.Contains(snapshot.RootElement.EnumerateArray(), row =>
                row.GetProperty("sessionId").GetInt32() == spid
                && row.GetProperty("ownerType").GetString() == "TRANSACTION"
                && row.GetProperty("lockMode").GetString() == "X"
                && row.GetProperty("connectionId").GetGuid() == connectionId
                && row.GetProperty("sessionOpenTransactionCount").GetInt32() > 0);
            await transaction.RollbackAsync();
        }
        Assert.Equal(0, await holder.Database.SqlQueryRaw<int>("SELECT @@TRANCOUNT AS [Value]").SingleAsync());
        await holder.Database.CloseConnectionAsync();
        var after = string.Concat(await IdentityDatabaseVerifier.ReadRowsAsync(observer, FixtureLockDiagnostics.SnapshotSql));
        using var released = JsonDocument.Parse(after);
        // Pooling không được coi là lý do bỏ qua lock transaction thật.
        Assert.DoesNotContain(released.RootElement.EnumerateArray(), row =>
            row.GetProperty("sessionId").GetInt32() == spid && row.GetProperty("ownerType").GetString() == "TRANSACTION");
    }
}
