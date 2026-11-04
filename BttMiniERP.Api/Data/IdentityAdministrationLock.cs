using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Api.Data;

public static class IdentityAdministrationLock
{
    public static Task AcquireAsync(ApplicationDbContext context, CancellationToken cancellationToken)
        => context.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource=N'BttMiniERP.Identity.Administration',
                @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51010, 'Identity administration lock unavailable.', 1;
            """, cancellationToken);
}
