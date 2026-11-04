using BttMiniERP.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Tests.Integration;

[Collection("SQL Server")]
[Trait("Category", "SqlServer")]
[Trait("SqlSuite", "MigrationDown")]
public sealed class MigrationDownTests(SqlServerFixture fixture)
{
    [SqlServerFact]
    public async Task OriginalSchemaAccepts129CharacterGraphAndRollsBackAllRecords()
    {
        fixture.RequireVerification("MigrationDownCheck", "After");
        await using var context = fixture.CreateContext();
        await fixture.ApplyMigrationsAsync(context); // Xác minh 450/history gốc, KHÔNG migrate.
        var before = await IdentityDatabaseVerifier.CaptureDataAsync(context);
        var data = new IdentityTestData(129);
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            // Raw SQL tránh model 128 vô tình che schema Down 450.
            await data.InsertGraphAsync(context);
            var afterInsert = await IdentityDatabaseVerifier.CaptureDataAsync(context);
            foreach (var table in new[] { "AspNetUsers", "AspNetRoles", "AspNetUserRoles", "AspNetUserLogins", "AspNetUserTokens" })
                Assert.Equal(before.Single(t => t.Table == table).Rows + 1, afterInsert.Single(t => t.Table == table).Rows);
            await transaction.RollbackAsync();
        }
        Assert.Equal(before, await IdentityDatabaseVerifier.CaptureDataAsync(context));
    }
}
