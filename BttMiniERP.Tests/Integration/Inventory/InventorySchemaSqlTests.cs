using BttMiniERP.Api.Data;
using BttMiniERP.Api.Data.Migrations;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
using BttMiniERP.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BttMiniERP.Tests.Integration.Inventory;

[Collection("MasterData SQL"), Trait("Category", "SqlServer")]
public sealed class InventorySchemaSqlTests
{
    [InventorySqlFact]
    [Trait("SqlSuite", "Inventory")]
    public async Task SchemaHistoryAndOwnedCleanupMatchModelWithoutLeakedTransactionsOrLocks()
    {
        var f = await MasterDataSqlFixture.OpenAsync("Inventory");
        try
        {
            await using var db = f.Context(); await db.Database.OpenConnectionAsync();
            Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
            var pk = await IdentityDatabaseVerifier.ReadRowsAsync(db, "SELECT t.name,c.name FROM sys.key_constraints k JOIN sys.tables t ON t.object_id=k.parent_object_id JOIN sys.index_columns ic ON ic.object_id=t.object_id AND ic.index_id=k.unique_index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE t.name IN ('InventoryBalances','InventoryTransactions') AND k.type='PK' ORDER BY t.name");
            Assert.Equal(new[] { "InventoryBalances|ItemId", "InventoryTransactions|Id" }, pk);
            var fks = await IdentityDatabaseVerifier.ReadRowsAsync(db, "SELECT f.name,c.name,OBJECT_NAME(f.referenced_object_id),p.name,f.delete_referential_action_desc,CAST(f.is_disabled AS int),CAST(f.is_not_trusted AS int) FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns p ON p.object_id=fc.referenced_object_id AND p.column_id=fc.referenced_column_id WHERE f.parent_object_id IN (OBJECT_ID('dbo.InventoryBalances'),OBJECT_ID('dbo.InventoryTransactions')) ORDER BY f.name");
            Assert.Equal(new[] {
                "FK_InventoryBalances_AspNetUsers_CreatedByUserId|CreatedByUserId|AspNetUsers|Id|NO_ACTION|0|0",
                "FK_InventoryBalances_AspNetUsers_UpdatedByUserId|UpdatedByUserId|AspNetUsers|Id|NO_ACTION|0|0",
                "FK_InventoryBalances_Items_ItemId|ItemId|Items|Id|NO_ACTION|0|0",
                "FK_InventoryTransactions_AspNetUsers_PerformedByUserId|PerformedByUserId|AspNetUsers|Id|NO_ACTION|0|0",
                "FK_InventoryTransactions_Items_ItemId|ItemId|Items|Id|NO_ACTION|0|0" }, fks);
            var indexes = await IdentityDatabaseVerifier.ReadRowsAsync(db, "SELECT i.name,c.name,ic.key_ordinal,CAST(i.is_unique AS int),CAST(i.is_disabled AS int) FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id IN (OBJECT_ID('dbo.InventoryBalances'),OBJECT_ID('dbo.InventoryTransactions')) ORDER BY i.name,ic.key_ordinal");
            Assert.Equal(new[] {
                "IX_InventoryBalances_CreatedByUserId|CreatedByUserId|1|0|0", "IX_InventoryBalances_UpdatedByUserId|UpdatedByUserId|1|0|0",
                "IX_InventoryTransactions_ItemId_OccurredAt_Id|ItemId|1|0|0", "IX_InventoryTransactions_ItemId_OccurredAt_Id|OccurredAt|2|0|0", "IX_InventoryTransactions_ItemId_OccurredAt_Id|Id|3|0|0",
                "IX_InventoryTransactions_PerformedByUserId|PerformedByUserId|1|0|0",
                "IX_InventoryTransactions_Source|SourceDocumentType|1|1|0", "IX_InventoryTransactions_Source|SourceDocumentId|2|1|0", "IX_InventoryTransactions_Source|SourceLineId|3|1|0", "IX_InventoryTransactions_Source|TransactionType|4|1|0",
                "PK_InventoryBalances|ItemId|1|1|0", "PK_InventoryTransactions|Id|1|1|0" }, indexes);
            var checks = await IdentityDatabaseVerifier.ReadRowsAsync(db, "SELECT name,CAST(is_disabled AS int),CAST(is_not_trusted AS int) FROM sys.check_constraints WHERE parent_object_id IN (OBJECT_ID('dbo.InventoryBalances'),OBJECT_ID('dbo.InventoryTransactions')) ORDER BY name");
            Assert.Equal(new[] { "CK_InventoryBalances_Quantity|0|0", "CK_InventoryTransactions_BalanceAfter|0|0", "CK_InventoryTransactions_Delta|0|0", "CK_InventoryTransactions_Source|0|0" }, checks);
            var category = await f.Categories(db).CreateAsync(new() { Code = "CLEAN", Name = "Cleanup" }, default);
            var item = await f.Items(db).CreateAsync(new() { Code = "CLEAN", Name = "Cleanup", CategoryId = category.Id, Unit = "kg", ItemType = ItemType.RawMaterial }, default);
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                await f.Inventory(db).ApplyAsync([new(item.Id, 1, InventoryTransactionType.PurchaseReceipt, SourceDocumentType.PurchaseReceipt, 1, 1)], default);
                await tx.CommitAsync();
            }
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT @@TRANCOUNT AS [Value]").SingleAsync());
        }
        finally { await f.DisposeAsync(); }
        // Chỉ metadata của đúng database đã guard; VIEW SERVER PERFORMANCE STATE có thể cần cho DMV.
        await using var verify = f.Context(); await verify.Database.OpenConnectionAsync();
        Assert.Empty(await verify.InventoryTransactions.ToArrayAsync()); Assert.Empty(await verify.InventoryBalances.ToArrayAsync());
        Assert.Empty(await verify.Items.ToArrayAsync()); Assert.Empty(await verify.Categories.ToArrayAsync()); Assert.Empty(await verify.Users.ToArrayAsync());
        Assert.Empty(await IdentityDatabaseVerifier.ReadRowsAsync(verify,
            "SELECT request_session_id FROM sys.dm_tran_locks WHERE resource_database_id=DB_ID() AND request_session_id<>@@SPID AND (resource_type='APPLICATION' OR request_owner_type='TRANSACTION')"));
    }

    [InventorySqlFact(migration: true)]
    [Trait("SqlSuite", "InventoryMigration")]
    public async Task ActualMigrationOperationsBackfillLegacyItemWithZeroAndRollbackDdlWithoutChangingHistory()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("InventoryMigration");
        await using var db = f.Context(); await db.Database.OpenConnectionAsync();
        var history = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(4, history.Length);
        var category = await f.Categories(db).CreateAsync(new() { Code = "LEGACY", Name = "Legacy" }, default);
        var legacyItems = new[] { ItemType.RawMaterial, ItemType.FinishedProduct }.Select(type => new Item {
            Code = "LEGACY-" + (int)type, Name = "Legacy " + type, CategoryId = category.Id, Unit = "kg", ItemType = type,
            PurchasePrice = 12.34m, SalePrice = 56.78m, IsActive = true, CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = f.ActorId, UpdatedAt = DateTimeOffset.UtcNow, UpdatedByUserId = f.ActorId }).ToArray();
        db.Items.AddRange(legacyItems);
        await db.SaveChangesAsync(); // Baseline thật đã commit, chưa có balance; fixture ghi nhận ownership.
        const string itemSnapshot = "SELECT Id,Code,Name,CategoryId,Unit,ItemType,PurchasePrice,SalePrice,IsActive,CreatedAt,CreatedByUserId,UpdatedAt,UpdatedByUserId,CONVERT(varchar(18),RowVersion,1) FROM Items ORDER BY Id";
        var before = await IdentityDatabaseVerifier.ReadRowsAsync(db, itemSnapshot);
        var schemaBefore = await SchemaSnapshot(db);
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var migration = new AddInventoryLedger();
            var commands = db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, db.GetService<IDesignTimeModel>().Model);
            foreach (var command in commands)
            {
                Assert.False(command.TransactionSuppressed);
                await db.Database.ExecuteSqlRawAsync(command.CommandText);
            }
            var balances = await db.InventoryBalances.AsNoTracking().OrderBy(x => x.ItemId).ToArrayAsync();
            Assert.Equal(legacyItems.Select(x => x.Id).Order(), balances.Select(x => x.ItemId));
            foreach (var balance in balances)
            {
                var legacy = legacyItems.Single(x => x.Id == balance.ItemId);
                Assert.Equal(0m, balance.QuantityOnHand); Assert.Equal(legacy.CreatedAt, balance.CreatedAt);
                Assert.Equal(f.ActorId, balance.CreatedByUserId); Assert.Null(balance.UpdatedAt);
                Assert.Null(balance.UpdatedByUserId); Assert.Equal(8, balance.RowVersion.Length);
            }
            Assert.Equal(new[] { "PK|2", "FK|5", "CHECK|4", "INDEX|5" }, await IdentityDatabaseVerifier.ReadRowsAsync(db, """
                SELECT 'PK',COUNT(*) FROM sys.key_constraints WHERE parent_object_id IN (OBJECT_ID('InventoryBalances'),OBJECT_ID('InventoryTransactions'))
                UNION ALL SELECT 'FK',COUNT(*) FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID('InventoryBalances'),OBJECT_ID('InventoryTransactions')) AND is_disabled=0 AND is_not_trusted=0
                UNION ALL SELECT 'CHECK',COUNT(*) FROM sys.check_constraints WHERE parent_object_id IN (OBJECT_ID('InventoryBalances'),OBJECT_ID('InventoryTransactions')) AND is_disabled=0 AND is_not_trusted=0
                UNION ALL SELECT 'INDEX',COUNT(*) FROM sys.indexes WHERE object_id IN (OBJECT_ID('InventoryBalances'),OBJECT_ID('InventoryTransactions')) AND is_primary_key=0 AND is_disabled=0
                """));
            Assert.Empty(await db.InventoryTransactions.ToArrayAsync());
            Assert.Equal(before, await IdentityDatabaseVerifier.ReadRowsAsync(db, itemSnapshot));
            Assert.Equal(history, await db.Database.GetAppliedMigrationsAsync());
            await tx.RollbackAsync();
        }
        await using var verify = f.Context(); await verify.Database.OpenConnectionAsync();
        Assert.Equal(history, await verify.Database.GetAppliedMigrationsAsync());
        Assert.Equal(before, await IdentityDatabaseVerifier.ReadRowsAsync(verify, itemSnapshot));
        Assert.Equal(schemaBefore, await SchemaSnapshot(verify));
        Assert.Empty(await IdentityDatabaseVerifier.ReadRowsAsync(verify, "SELECT name FROM sys.tables WHERE name IN ('InventoryBalances','InventoryTransactions')"));
        Assert.Equal(0, await verify.Database.SqlQueryRaw<int>("SELECT @@TRANCOUNT AS [Value]").SingleAsync());
    }

    private static Task<List<string>> SchemaSnapshot(ApplicationDbContext db) => IdentityDatabaseVerifier.ReadRowsAsync(db, """
        SELECT CONCAT('C|',t.name,'|',c.name,'|',c.system_type_id,'|',c.max_length,'|',c.precision,'|',c.scale,'|',c.is_nullable)
        FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id
        UNION ALL SELECT CONCAT('I|',OBJECT_NAME(i.object_id),'|',i.name,'|',c.name,'|',ic.key_ordinal,'|',i.is_unique,'|',i.is_disabled)
        FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
        UNION ALL SELECT CONCAT('F|',name,'|',OBJECT_NAME(parent_object_id),'|',OBJECT_NAME(referenced_object_id),'|',delete_referential_action,'|',is_disabled,'|',is_not_trusted) FROM sys.foreign_keys
        UNION ALL SELECT CONCAT('K|',name,'|',definition,'|',is_disabled,'|',is_not_trusted) FROM sys.check_constraints
        ORDER BY 1
        """);
}
