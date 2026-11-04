using System.Data.Common;
using BttMiniERP.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BttMiniERP.Tests.Fixtures;

// Chỉ quan sát đúng hai session test trong database fixture đã được kiểm tra.
internal static class BootstrapLockEvidence
{
    public static async Task WaitForWaiterAsync(BootstrapSqlFixture fixture, DbConnection holder, DbConnection waiter)
    {
        var holderId = ((SqlConnection)holder).ServerProcessId;
        var waiterId = ((SqlConnection)waiter).ServerProcessId;
        Assert.NotEqual(holderId, waiterId);
        using var provider = fixture.CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await db.Database.OpenConnectionAsync(deadline.Token);
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandTimeout = 5;
            command.CommandText = """
                SELECT COUNT(*)
                FROM sys.dm_exec_requests r
                JOIN sys.dm_tran_locks w ON w.request_session_id = r.session_id
                JOIN sys.dm_tran_locks h ON h.resource_database_id = w.resource_database_id
                    AND h.resource_type = w.resource_type AND h.resource_description = w.resource_description
                    AND h.resource_associated_entity_id = w.resource_associated_entity_id
                WHERE r.session_id = @waiter AND r.blocking_session_id = @holder
                  AND r.database_id = DB_ID() AND r.wait_type LIKE N'LCK_M_%'
                  AND w.resource_database_id = DB_ID() AND w.resource_type = N'APPLICATION'
                  AND w.request_status = N'WAIT' AND w.request_mode = N'X'
                  AND w.request_owner_type = N'TRANSACTION'
                  AND h.request_session_id = @holder AND h.request_status = N'GRANT'
                  AND h.request_mode = N'X' AND h.request_owner_type = N'TRANSACTION';
                """;
            command.Parameters.Add(new SqlParameter("@holder", holderId));
            command.Parameters.Add(new SqlParameter("@waiter", waiterId));
            while (true)
            {
                if (Convert.ToInt32(await command.ExecuteScalarAsync(deadline.Token)) > 0) return;
                // Chỉ nhịp polling; bằng chứng là hàng DMV khớp waiter/blocker/resource, không phải delay.
                await Task.Delay(50, deadline.Token);
            }
        }
        catch (Exception error) when (error is SqlException or OperationCanceledException)
        {
            throw new InvalidOperationException(
                "Không xác minh được SQL APPLICATION lock waiter/blocker trong 5 giây. Kiểm tra quyền DMV và lịch chạy; không coi là PASS.");
        }
    }

    public static async Task AssertNoLocksAsync(ApplicationDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandTimeout = 5;
        command.CommandText = """
            SELECT @@TRANCOUNT + COUNT(*)
            FROM sys.dm_tran_locks
            WHERE request_session_id = @@SPID AND resource_database_id = DB_ID()
              AND resource_type = N'APPLICATION';
            """;
        Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }
}
