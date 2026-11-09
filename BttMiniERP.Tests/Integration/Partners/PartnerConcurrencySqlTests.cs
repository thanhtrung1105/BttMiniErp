using BttMiniERP.Api.Common;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.Entities;
using BttMiniERP.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Tests.Integration.Partners;

public sealed partial class PartnersSqlTests
{
    [PartnersSqlFact]
    public async Task ExplicitTransactionRollsBackBothPartnerWrites()
    {
        await using var fixture = await PartnersSqlFixture.OpenAsync();
        await using (var db = fixture.Context())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await fixture.Suppliers(db).CreateAsync(new() { Code = "rollback", Name = "Supplier" }, Ct);
            await fixture.Customers(db).CreateAsync(new() { Code = "rollback", Name = "Customer" }, Ct);
            Assert.Equal(1, await db.Suppliers.CountAsync());
            Assert.Equal(1, await db.Customers.CountAsync());
            await transaction.RollbackAsync();
        }
        await using var verify = fixture.Context();
        Assert.Empty(await verify.Suppliers.ToListAsync());
        Assert.Empty(await verify.Customers.ToListAsync());
    }

    [PartnersSqlFact]
    public async Task ConcurrentSupplierUpdatesPreserveWinnerAndRejectStaleDelete()
    {
        await using var fixture = await PartnersSqlFixture.OpenAsync();
        await using var setup = fixture.Context();
        var row = await fixture.Suppliers(setup).CreateAsync(new() { Code = "race", Name = "Before" }, Ct);
        var barrier = new TwoSaves();
        await using var first = fixture.Context(barrier);
        await using var second = fixture.Context(barrier);
        async Task<(int Status, string Name)> Change(ApplicationDbContext db, string name)
        {
            try { await fixture.Suppliers(db).UpdateAsync(row.Id, new() { Code = row.Code, Name = name, RowVersion = row.RowVersion }, Ct); return (200, name); }
            catch (ApiException error) { return (error.StatusCode, name); }
        }
        var results = await Task.WhenAll(Change(first, "First"), Change(second, "Second"));
        Assert.Equal(new[] { 200, 409 }, results.Select(x => x.Status).Order());
        await using var verify = fixture.Context();
        var persisted = await verify.Suppliers.SingleAsync();
        Assert.Equal(results.Single(x => x.Status == 200).Name, persisted.Name);
        await Conflict(() => fixture.Suppliers(verify).DeactivateAsync(row.Id, row.RowVersion, Ct));
        await using var reread = fixture.Context();
        Assert.True((await reread.Suppliers.SingleAsync()).IsActive);
    }

    [PartnersSqlFact]
    public async Task ConcurrentCustomerUpdatesPreserveWinnerAndRejectStaleDelete()
    {
        await using var fixture = await PartnersSqlFixture.OpenAsync();
        await using var setup = fixture.Context();
        var row = await fixture.Customers(setup).CreateAsync(new() { Code = "race", Name = "Before" }, Ct);
        var barrier = new TwoSaves();
        await using var first = fixture.Context(barrier);
        await using var second = fixture.Context(barrier);
        async Task<(int Status, string Name)> Change(ApplicationDbContext db, string name)
        {
            try { await fixture.Customers(db).UpdateAsync(row.Id, new() { Code = row.Code, Name = name, RowVersion = row.RowVersion }, Ct); return (200, name); }
            catch (ApiException error) { return (error.StatusCode, name); }
        }
        var results = await Task.WhenAll(Change(first, "First"), Change(second, "Second"));
        Assert.Equal(new[] { 200, 409 }, results.Select(x => x.Status).Order());
        await using var verify = fixture.Context();
        Assert.Equal(results.Single(x => x.Status == 200).Name, (await verify.Customers.SingleAsync()).Name);
        await Conflict(() => fixture.Customers(verify).DeactivateAsync(row.Id, row.RowVersion, Ct));
        await using var reread = fixture.Context();
        Assert.True((await reread.Customers.SingleAsync()).IsActive);
    }

    [PartnersSqlFact]
    public async Task UniqueIndexesAndAuditForeignKeysAreEnforcedBySqlServer()
    {
        await using var fixture = await PartnersSqlFixture.OpenAsync();
        await using var setup = fixture.Context();
        await fixture.Suppliers(setup).CreateAsync(new() { Code = "same", Name = "Supplier" }, Ct);
        await fixture.Customers(setup).CreateAsync(new() { Code = "same", Name = "Customer" }, Ct);
        foreach (var supplier in new[] { true, false })
        foreach (var duplicate in new[] { true, false })
        {
            await using var db = fixture.Context();
            // Service đã lưu SAME; ghi trực tiếp chữ thường để kiểm chứng collation/unique SQL.
            var code = duplicate ? "same" : "ORPHAN";
            var actor = duplicate ? fixture.ActorId : "nonexistent-actor";
            if (supplier) db.Suppliers.Add(new Supplier { Code = code, Name = "Rejected", CreatedByUserId = actor, CreatedAt = DateTimeOffset.UtcNow });
            else db.Customers.Add(new Customer { Code = code, Name = "Rejected", CreatedByUserId = actor, CreatedAt = DateTimeOffset.UtcNow });
            // Unique đã có test race qua service; lỗi FK không được biến thành 409.
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate
                ? db.SaveChangesAsync() : BttMiniERP.Api.Services.MasterDataRules.SaveAsync(db, Ct));
            var number = Assert.IsType<SqlException>(error.InnerException).Number;
            if (duplicate) Assert.Contains(number, new[] { 2601, 2627 });
            else Assert.Equal(547, number);
        }
        Assert.Equal(1, await setup.Suppliers.CountAsync());
        Assert.Equal(1, await setup.Customers.CountAsync());
    }

    [PartnersSqlFact]
    public async Task SchemaAndMigrationHistoryMatchPartnersModel()
    {
        await using var fixture = await PartnersSqlFixture.OpenAsync();
        await using var db = fixture.Context();
        await db.Database.OpenConnectionAsync();
        var history = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(4, history.Length);
        Assert.EndsWith("_AddPartners", history[^1]);
        Assert.Equal(db.Database.GetMigrations(), history);
        var keys = await IdentityDatabaseVerifier.ReadRowsAsync(db, "SELECT t.name,k.name,c.name FROM sys.key_constraints k JOIN sys.tables t ON t.object_id=k.parent_object_id JOIN sys.index_columns i ON i.object_id=t.object_id AND i.index_id=k.unique_index_id JOIN sys.columns c ON c.object_id=i.object_id AND c.column_id=i.column_id WHERE t.name IN ('Customers','Suppliers') AND k.type='PK' ORDER BY t.name");
        Assert.Equal(new[] { "Customers|PK_Customers|Id", "Suppliers|PK_Suppliers|Id" }, keys);
        var indexes = await IdentityDatabaseVerifier.ReadRowsAsync(db, "SELECT i.name,c.name,CAST(i.is_unique AS int),CAST(i.is_disabled AS int) FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id IN (OBJECT_ID('dbo.Customers'),OBJECT_ID('dbo.Suppliers')) AND i.is_primary_key=0 ORDER BY i.name");
        var expectedIndexes = new[] { "Customers", "Suppliers" }.SelectMany(table => new[] { "Code", "CreatedByUserId", "Name", "UpdatedByUserId" }.Select(column => $"IX_{table}_{column}|{column}|{(column == "Code" ? 1 : 0)}|0"));
        Assert.Equal(expectedIndexes, indexes);
        var fks = await IdentityDatabaseVerifier.ReadRowsAsync(db, "SELECT f.name,c.name,OBJECT_NAME(f.referenced_object_id),p.name,f.delete_referential_action_desc,CAST(f.is_disabled AS int),CAST(f.is_not_trusted AS int) FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns p ON p.object_id=fc.referenced_object_id AND p.column_id=fc.referenced_column_id WHERE f.parent_object_id IN (OBJECT_ID('dbo.Customers'),OBJECT_ID('dbo.Suppliers')) ORDER BY f.name");
        Assert.Equal(new[] { "Customers", "Suppliers" }.SelectMany(table => new[] { "CreatedByUserId", "UpdatedByUserId" }.Select(column => $"FK_{table}_AspNetUsers_{column}|{column}|AspNetUsers|Id|NO_ACTION|0|0")), fks);
        var versions = await IdentityDatabaseVerifier.ReadRowsAsync(db, "SELECT OBJECT_NAME(c.object_id),c.max_length,c.is_nullable FROM sys.columns c WHERE c.object_id IN (OBJECT_ID('dbo.Customers'),OBJECT_ID('dbo.Suppliers')) AND c.name='RowVersion' AND c.system_type_id=189 ORDER BY OBJECT_NAME(c.object_id)");
        Assert.Equal(new[] { "Customers|8|False", "Suppliers|8|False" }, versions);
    }
}
