using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json;
using BttMiniERP.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BttMiniERP.Tests.Fixtures;

// Chỉ metadata, không connection string, SQL text, password, token hoặc dữ liệu nghiệp vụ.
// Giữ nguyên pool và điều kiện phát hiện lock; không delay/retry/kill session.
internal sealed class FixtureLockDiagnostics : DbConnectionInterceptor
{
    private readonly ConcurrentQueue<object> lifecycle = new();

    public void Mark(string phase, ApplicationDbContext context)
        => lifecycle.Enqueue(new { utc = DateTimeOffset.UtcNow, phase, context = context.ContextId.ToString() });

    private void Record(string phase, DbConnection connection, ConnectionEventData data)
    {
        var sql = (SqlConnection)connection;
        var open = sql.State == System.Data.ConnectionState.Open;
        lifecycle.Enqueue(new { utc = DateTimeOffset.UtcNow, phase, context = data.Context?.ContextId.ToString(),
            efConnectionId = data.ConnectionId, spid = open ? (int?)sql.ServerProcessId : null,
            clientConnectionId = open ? (Guid?)sql.ClientConnectionId : null, state = sql.State.ToString() });
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData data) => Record("opened", connection, data);
    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData data, CancellationToken ct = default)
    { Record("opened", connection, data); return Task.CompletedTask; }
    public override InterceptionResult ConnectionClosing(DbConnection connection, ConnectionEventData data, InterceptionResult result)
    { Record("closing", connection, data); return result; }
    public override ValueTask<InterceptionResult> ConnectionClosingAsync(DbConnection connection, ConnectionEventData data, InterceptionResult result)
    { Record("closing", connection, data); return ValueTask.FromResult(result); }
    public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData data) => Record("closed", connection, data);
    public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData data)
    { Record("closed", connection, data); return Task.CompletedTask; }
    public override InterceptionResult ConnectionDisposing(DbConnection connection, ConnectionEventData data, InterceptionResult result)
    { Record("disposing", connection, data); return result; }
    public override ValueTask<InterceptionResult> ConnectionDisposingAsync(DbConnection connection, ConnectionEventData data, InterceptionResult result)
    { Record("disposing", connection, data); return ValueTask.FromResult(result); }
    public override void ConnectionDisposed(DbConnection connection, ConnectionEndEventData data) => Record("disposed", connection, data);
    public override Task ConnectionDisposedAsync(DbConnection connection, ConnectionEndEventData data)
    { Record("disposed", connection, data); return Task.CompletedTask; }

    public string Describe(string actor, string snapshot) => JsonSerializer.Serialize(new { actor, snapshot, lifecycle = lifecycle.ToArray() });

    // DMV thay đổi theo thời gian: giữ metadata cùng câu SELECT với lock gây fail,
    // không chỉ lưu SPID rồi truy vấn lại một session có thể đã được pool/tái sử dụng.
    internal const string SnapshotSql = """
        SELECT COALESCE((SELECT SYSUTCDATETIME() AS capturedUtc, @@SPID AS observerSpid, @@TRANCOUNT AS observerTranCount,
            DB_NAME() AS observedDatabase, l.request_session_id AS sessionId,
            l.resource_database_id AS resourceDatabaseId, l.resource_type AS resourceType,
            l.resource_subtype AS resourceSubtype, l.resource_description AS resourceDescription,
            l.resource_associated_entity_id AS resourceEntityId,
            l.request_owner_type AS ownerType, l.request_owner_id AS ownerId,
            l.request_mode AS lockMode, l.request_status AS lockStatus,
            s.is_user_process AS isUserProcess, s.login_name AS loginName, s.login_time AS loginTime,
            s.host_name AS hostName, s.program_name AS programName, s.host_process_id AS hostProcessId,
            s.status AS sessionStatus, s.open_transaction_count AS sessionOpenTransactionCount,
            s.last_request_start_time AS lastRequestStart, s.last_request_end_time AS lastRequestEnd,
            c.connection_id AS connectionId, c.connect_time AS connectTime, c.net_transport AS transport,
            r.request_id AS requestId, r.database_id AS requestDatabaseId,
            r.command AS requestCommand, r.status AS requestStatus, r.blocking_session_id AS blockingSessionId,
            r.wait_type AS waitType, r.wait_resource AS waitResource,
            r.open_transaction_count AS requestOpenTransactionCount, r.transaction_id AS requestTransactionId,
            at.transaction_id AS activeTransactionId, at.name AS transactionName,
            at.transaction_type AS transactionType, at.transaction_state AS transactionState,
            at.transaction_begin_time AS transactionBeginTime
        FROM sys.dm_tran_locks AS l
        LEFT JOIN sys.dm_exec_sessions AS s ON s.session_id=l.request_session_id
        LEFT JOIN sys.dm_exec_connections AS c ON c.session_id=l.request_session_id
        LEFT JOIN sys.dm_exec_requests AS r ON r.session_id=l.request_session_id
        LEFT JOIN sys.dm_tran_active_transactions AS at
            ON l.request_owner_type='TRANSACTION' AND at.transaction_id=l.request_owner_id
        WHERE l.resource_database_id=DB_ID()
          AND (l.resource_type='APPLICATION'
            OR (l.request_owner_type='TRANSACTION' AND l.request_session_id<>@@SPID
              AND NOT EXISTS (SELECT 1 FROM sys.dm_exec_sessions AS systemSession
                WHERE systemSession.session_id=l.request_session_id AND systemSession.is_user_process=0)))
        FOR JSON PATH, INCLUDE_NULL_VALUES), N'[]') AS Snapshot
        """;
}
