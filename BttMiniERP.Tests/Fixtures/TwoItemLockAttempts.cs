using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BttMiniERP.Tests.Fixtures;

// Đồng bộ trước khi SQL lấy khóa, không chờ nhau bên trong vùng đã giữ khóa.
public sealed class TwoItemLockAttempts : DbCommandInterceptor
{
    private int arrived;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("FROM [Items] WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal)
            && Interlocked.Increment(ref arrived) <= 2)
        {
            if (Volatile.Read(ref arrived) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        return result;
    }
}
