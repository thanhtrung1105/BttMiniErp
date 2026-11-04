using BttMiniERP.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Tests.Integration;

[Collection("SQL Server")]
[Trait("Category", "SqlServer")]
[Trait("SqlSuite", "Persistence")]
public sealed class IdentitySqlBoundaryTests(SqlServerFixture fixture)
{
    [SqlServerFact]
    public async Task EveryStringKeyRejects129WithoutTruncationOrFkConfusion()
    {
        await using var context = fixture.CreateContext();
        await fixture.PrepareConstraintChecksAsync(context, require128: true);
        var before = await IdentityDatabaseVerifier.CaptureDataAsync(context);
        var targets = new[] { ("AspNetUsers", "Id"), ("AspNetRoles", "Id"),
            ("AspNetUserRoles", "UserId"), ("AspNetUserRoles", "RoleId"),
            ("AspNetUserLogins", "UserId"), ("AspNetUserLogins", "LoginProvider"), ("AspNetUserLogins", "ProviderKey"),
            ("AspNetUserTokens", "UserId"), ("AspNetUserTokens", "LoginProvider"), ("AspNetUserTokens", "Name"),
            ("AspNetUserClaims", "UserId"), ("AspNetRoleClaims", "RoleId") };
        foreach (var (table, column) in targets)
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var data = new IdentityTestData(128);
            await data.InsertParentsAsync(context);
            var sql = table switch
            {
                "AspNetUsers" => "INSERT INTO dbo.AspNetUsers(Id,EmailConfirmed,PhoneNumberConfirmed,TwoFactorEnabled,LockoutEnabled,AccessFailedCount) VALUES (@bad,0,0,0,0,0)",
                "AspNetRoles" => "INSERT INTO dbo.AspNetRoles(Id) VALUES (@bad)",
                "AspNetUserRoles" => $"INSERT INTO dbo.AspNetUserRoles(UserId,RoleId) VALUES ({(column == "UserId" ? "@bad" : "@u")},{(column == "RoleId" ? "@bad" : "@r")})",
                "AspNetUserLogins" => $"INSERT INTO dbo.AspNetUserLogins(UserId,LoginProvider,ProviderKey) VALUES ({(column == "UserId" ? "@bad" : "@u")},{(column == "LoginProvider" ? "@bad" : "@p")},{(column == "ProviderKey" ? "@bad" : "@k")})",
                "AspNetUserTokens" => $"INSERT INTO dbo.AspNetUserTokens(UserId,LoginProvider,Name) VALUES ({(column == "UserId" ? "@bad" : "@u")},{(column == "LoginProvider" ? "@bad" : "@p")},{(column == "Name" ? "@bad" : "@n")})",
                "AspNetUserClaims" => "INSERT INTO dbo.AspNetUserClaims(UserId) VALUES (@bad)",
                _ => "INSERT INTO dbo.AspNetRoleClaims(RoleId) VALUES (@bad)"
            };
            var prefix = column == "RoleId" ? data.RoleId : data.UserId;
            var error = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(sql,
                IdentityTestData.Parameter("@bad", prefix + "x"), IdentityTestData.Parameter("@u", data.UserId),
                IdentityTestData.Parameter("@r", data.RoleId), IdentityTestData.Parameter("@p", data.Provider),
                IdentityTestData.Parameter("@k", data.ProviderKey), IdentityTestData.Parameter("@n", data.TokenName)));
            Assert.Contains(error.Number, new[] { 2628, 8152 }); // Không chấp nhận lỗi FK 547 thay lỗi độ dài.
            await transaction.RollbackAsync();
        }
        Assert.Equal(before, await IdentityDatabaseVerifier.CaptureDataAsync(context));
    }
}
