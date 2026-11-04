using System.Data;
using BttMiniERP.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Tests.Fixtures;

public sealed class IdentityTestData(int length)
{
    public string UserId { get; } = Guid.NewGuid().ToString().PadRight(length, 'u');
    public string RoleId { get; } = Guid.NewGuid().ToString().PadRight(length, 'r');
    public string Provider { get; } = Guid.NewGuid().ToString().PadRight(length, 'p');
    public string ProviderKey { get; } = Guid.NewGuid().ToString().PadRight(length, 'k');
    public string TokenName { get; } = Guid.NewGuid().ToString().PadRight(length, 'n');

    public static SqlParameter Parameter(string name, string value) => new(name, SqlDbType.NVarChar, -1) { Value = value };

    public async Task InsertParentsAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO dbo.AspNetUsers(Id,UserName,EmailConfirmed,PhoneNumberConfirmed,TwoFactorEnabled,LockoutEnabled,AccessFailedCount)
            VALUES (@u,@u,0,0,0,0,0);
            INSERT INTO dbo.AspNetRoles(Id,Name) VALUES (@r,@r);
            """, Parameter("@u", UserId), Parameter("@r", RoleId));
    }

    public async Task InsertGraphAsync(ApplicationDbContext context)
    {
        await InsertParentsAsync(context);
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO dbo.AspNetUserRoles(UserId,RoleId) VALUES (@u,@r);
            INSERT INTO dbo.AspNetUserLogins(UserId,LoginProvider,ProviderKey,ProviderDisplayName) VALUES (@u,@p,@k,N'Test login');
            INSERT INTO dbo.AspNetUserTokens(UserId,LoginProvider,Name,Value) VALUES (@u,@p,@n,N'Test value');
            """, Parameter("@u", UserId), Parameter("@r", RoleId), Parameter("@p", Provider), Parameter("@k", ProviderKey), Parameter("@n", TokenName));
    }
}
