using BttMiniERP.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Tests.Integration;

[Collection("SQL Server")]
[Trait("Category", "SqlServer")]
[Trait("SqlSuite", "ForeignKeys")]
public sealed class IdentityForeignKeyTests(SqlServerFixture fixture)
{
    [SqlServerFact]
    public async Task AllSixForeignKeysRejectOrphansAndPreventCascadeDeletion()
    {
        await using var context = fixture.CreateContext();
        await fixture.PrepareConstraintChecksAsync(context, require128: false);
        var before = await IdentityDatabaseVerifier.CaptureDataAsync(context);
        var edges = new[] { ("AspNetUserClaims", "UserId", "AspNetUsers"), ("AspNetUserLogins", "UserId", "AspNetUsers"),
            ("AspNetUserTokens", "UserId", "AspNetUsers"), ("AspNetUserRoles", "UserId", "AspNetUsers"),
            ("AspNetUserRoles", "RoleId", "AspNetRoles"), ("AspNetRoleClaims", "RoleId", "AspNetRoles") };
        foreach (var (table, column, parent) in edges)
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.Database.ExecuteSqlRawAsync("SET XACT_ABORT OFF;"); // Cho phép kiểm tra tiếp sau lỗi FK dự kiến, cuối cùng luôn rollback.
            var data = new IdentityTestData(128);
            await data.InsertParentsAsync(context);
            var sql = table switch
            {
                "AspNetUserClaims" => "INSERT INTO dbo.AspNetUserClaims(UserId) VALUES (@u)",
                "AspNetRoleClaims" => "INSERT INTO dbo.AspNetRoleClaims(RoleId) VALUES (@r)",
                "AspNetUserRoles" => "INSERT INTO dbo.AspNetUserRoles(UserId,RoleId) VALUES (@u,@r)",
                "AspNetUserLogins" => "INSERT INTO dbo.AspNetUserLogins(UserId,LoginProvider,ProviderKey) VALUES (@u,N'provider',N'key')",
                _ => "INSERT INTO dbo.AspNetUserTokens(UserId,LoginProvider,Name) VALUES (@u,N'provider',N'token')"
            };
            var missing = Guid.NewGuid().ToString().PadRight(128, 'z');
            var expectedFk = $"FK_{table}_{parent}_{column}";
            var orphan = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(sql,
                IdentityTestData.Parameter("@u", column == "UserId" ? missing : data.UserId),
                IdentityTestData.Parameter("@r", column == "RoleId" ? missing : data.RoleId)));
            Assert.Equal(547, orphan.Number);
            Assert.Contains(expectedFk, orphan.Message);
            await context.Database.ExecuteSqlRawAsync(sql, IdentityTestData.Parameter("@u", data.UserId), IdentityTestData.Parameter("@r", data.RoleId));
            // Chỉ thử xóa parent do chính test vừa tạo; SQL phải từ chối, sau đó rollback.
            var deleteSql = parent == "AspNetUsers"
                ? "DELETE FROM dbo.AspNetUsers WHERE Id=@id"
                : "DELETE FROM dbo.AspNetRoles WHERE Id=@id";
            var deletion = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(
                deleteSql, IdentityTestData.Parameter("@id", column == "UserId" ? data.UserId : data.RoleId)));
            Assert.Equal(547, deletion.Number);
            Assert.Contains(expectedFk, deletion.Message);
            await transaction.RollbackAsync();
        }
        Assert.Equal(before, await IdentityDatabaseVerifier.CaptureDataAsync(context));
    }
}
