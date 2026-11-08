using BttMiniERP.Api.Common;
using BttMiniERP.Api.DTOs.Categories;
using BttMiniERP.Api.DTOs.Items;
using BttMiniERP.Api.DTOs.MasterData;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
using BttMiniERP.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BttMiniERP.Tests.Integration.MasterData;

[Collection("MasterData SQL"), Trait("Category", "SqlServer"), Trait("SqlSuite", "MasterData")]
public sealed class MasterDataSqlTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [MasterDataSqlFact]
    public async Task CategoryCrudNormalizesFiltersPaginatesAndPreservesAudit()
    {
        await using var fixture = await MasterDataSqlFixture.OpenAsync();
        await using var db = fixture.Context();
        var service = fixture.Categories(db);
        var first = await service.CreateAsync(new() { Code = " cat-a ", Name = " First " }, Ct);
        var second = await service.CreateAsync(new() { Code = "cat-b", Name = "Second" }, Ct);
        Assert.Equal("CAT-A", first.Code);
        Assert.Equal("First", first.Name);
        Assert.Equal(fixture.ActorId, first.CreatedByUserId);
        Assert.Equal(TimeSpan.Zero, first.CreatedAt.Offset);
        Assert.Equal(8, Convert.FromBase64String(first.RowVersion).Length);
        Assert.Equal(first, await service.GetAsync(first.Id, Ct));
        var page = await service.ListAsync(new() { Page = 2, PageSize = 1, SortBy = "code" }, Ct);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(second.Id, Assert.Single(page.Items).Id);
        Assert.Equal(first.Id, Assert.Single((await service.ListAsync(new() { Search = "cat-a" }, Ct)).Items).Id);
        var updated = await service.UpdateAsync(first.Id, new() { Code = first.Code, Name = "Updated", RowVersion = first.RowVersion }, Ct);
        Assert.Equal("Updated", updated.Name);
        Assert.Equal(first.CreatedAt, updated.CreatedAt);
        Assert.Equal(fixture.ActorId, updated.UpdatedByUserId);
        Assert.NotEqual(first.RowVersion, updated.RowVersion);
        await Conflict(() => service.UpdateAsync(first.Id, new() { Code = first.Code, Name = "stale", RowVersion = first.RowVersion }, Ct));
        await using var fresh = fixture.Context();
        var inactive = await fixture.Categories(fresh).DeactivateAsync(first.Id, updated.RowVersion, Ct);
        Assert.False(inactive.IsActive);
        Assert.Equal(first.Id, Assert.Single((await fixture.Categories(fresh).ListAsync(new() { IsActive = false }, Ct)).Items).Id);
        Assert.Equal(2, await fresh.Categories.CountAsync());
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => service.GetAsync(int.MaxValue, Ct))).StatusCode);
        await Conflict(() => service.CreateAsync(new() { Code = " cat-b ", Name = "Duplicate" }, Ct));
    }

    [MasterDataSqlFact]
    public async Task ItemCrudSupportsBothTypesCategoryFilterAuditAndSoftDelete()
    {
        await using var fixture = await MasterDataSqlFixture.OpenAsync();
        await using var db = fixture.Context();
        var category = await fixture.Categories(db).CreateAsync(new() { Code = "cat", Name = "Category" }, Ct);
        var service = fixture.Items(db);
        var raw = await service.CreateAsync(ItemRequest(category.Id, " raw ", ItemType.RawMaterial), Ct);
        var product = await service.CreateAsync(ItemRequest(category.Id, "product", ItemType.FinishedProduct), Ct);
        Assert.Equal("RAW", raw.Code);
        Assert.Equal(12.34m, raw.PurchasePrice);
        Assert.Equal(fixture.ActorId, raw.CreatedByUserId);
        Assert.Equal(raw, await service.GetAsync(raw.Id, Ct));
        var page = await service.ListAsync(new() { CategoryId = category.Id, ItemType = ItemType.FinishedProduct, IsActive = true }, Ct);
        Assert.Equal(product.Id, Assert.Single(page.Items).Id);
        Assert.Equal(raw.Id, Assert.Single((await service.ListAsync(new() { Search = "ra", PageSize = 1 }, Ct)).Items).Id);
        var updated = await service.UpdateAsync(raw.Id, Update(raw, "Renamed"), Ct);
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal(raw.CreatedAt, updated.CreatedAt);
        Assert.Equal(fixture.ActorId, updated.UpdatedByUserId);
        Assert.NotEqual(raw.RowVersion, updated.RowVersion);
        await Conflict(() => service.DeactivateAsync(raw.Id, raw.RowVersion, Ct));
        await using var fresh = fixture.Context();
        var inactive = await fixture.Items(fresh).DeactivateAsync(raw.Id, updated.RowVersion, Ct);
        Assert.False(inactive.IsActive);
        Assert.Equal(2, await fresh.Items.CountAsync());
        Assert.Equal(raw.Id, Assert.Single((await fixture.Items(fresh).ListAsync(new() { IsActive = false }, Ct)).Items).Id);
        await Conflict(() => fixture.Items(fresh).CreateAsync(ItemRequest(category.Id, " PRODUCT ", ItemType.RawMaterial), Ct));
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => fixture.Items(fresh).GetAsync(int.MaxValue, Ct))).StatusCode);
    }

    [MasterDataSqlFact]
    public async Task InactiveCategoryCannotBeAssignedButExistingItemsStayActiveAndEditable()
    {
        await using var fixture = await MasterDataSqlFixture.OpenAsync();
        await using var db = fixture.Context();
        var category = await fixture.Categories(db).CreateAsync(new() { Code = "cat", Name = "Category" }, Ct);
        var other = await fixture.Categories(db).CreateAsync(new() { Code = "other", Name = "Other" }, Ct);
        var item = await fixture.Items(db).CreateAsync(ItemRequest(category.Id, "item", ItemType.RawMaterial), Ct);
        await fixture.Categories(db).DeactivateAsync(category.Id, category.RowVersion, Ct);
        Assert.True((await fixture.Items(db).GetAsync(item.Id, Ct)).IsActive);
        await Conflict(() => fixture.Items(db).CreateAsync(ItemRequest(category.Id, "rejected", ItemType.RawMaterial), Ct));
        var edited = await fixture.Items(db).UpdateAsync(item.Id, Update(item, "Still editable"), Ct);
        Assert.Equal("Still editable", edited.Name);
        var moved = await fixture.Items(db).UpdateAsync(item.Id, Update(edited, "Moved", other.Id), Ct);
        await Conflict(() => fixture.Items(db).UpdateAsync(item.Id, Update(moved, "Cannot return", category.Id), Ct));
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => fixture.Items(db).CreateAsync(ItemRequest(int.MaxValue, "missing", ItemType.RawMaterial), Ct))).StatusCode);
        Assert.Equal(1, await db.Items.CountAsync());
    }

    [MasterDataSqlFact]
    public async Task DatabaseEnforcesUniqueCodesForeignKeysChecksAndNoCascade()
    {
        await using var fixture = await MasterDataSqlFixture.OpenAsync();
        await using var db = fixture.Context();
        var category = await fixture.Categories(db).CreateAsync(new() { Code = "cat", Name = "Category" }, Ct);
        var item = await fixture.Items(db).CreateAsync(ItemRequest(category.Id, "item", ItemType.RawMaterial), Ct);
        await using (var check = fixture.Context())
        {
            check.Categories.Add(new Category { Code = category.Code, Name = "duplicate", CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = fixture.ActorId });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => check.SaveChangesAsync());
            Assert.Contains(Assert.IsType<SqlException>(error.InnerException).Number, new[] { 2601, 2627 });
        }
        await using (var check = fixture.Context())
        {
            check.Items.Add(new Item { Code = item.Code, Name = "duplicate", CategoryId = category.Id, Unit = "kg",
                ItemType = ItemType.RawMaterial, CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = fixture.ActorId });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => check.SaveChangesAsync());
            Assert.Contains(Assert.IsType<SqlException>(error.InnerException).Number, new[] { 2601, 2627 });
        }
        foreach (var mutation in new Action<Item>[] { x => x.CategoryId = int.MaxValue, x => x.ItemType = (ItemType)99,
            x => x.PurchasePrice = -1, x => x.SalePrice = -1, x => x.CreatedByUserId = "missing-actor" })
        {
            await using var check = fixture.Context();
            var entity = await check.Items.SingleAsync(x => x.Id == item.Id);
            mutation(entity);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => check.SaveChangesAsync());
            Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
        }
        await using (var check = fixture.Context())
        {
            check.Categories.Remove(await check.Categories.SingleAsync(x => x.Id == category.Id));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => check.SaveChangesAsync());
            Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
        }
        Assert.Equal(1, await db.Items.CountAsync());
        Assert.Equal(1, await db.Categories.CountAsync());
    }

    [MasterDataSqlFact]
    public async Task ConcurrentDuplicateCategoryReturnsOneSuccessAndOneConflict()
    {
        await using var fixture = await MasterDataSqlFixture.OpenAsync();
        var barrier = new TwoSaves();
        await using var first = fixture.Context(barrier);
        await using var second = fixture.Context(barrier);
        async Task<int> Create(BttMiniERP.Api.Data.ApplicationDbContext db)
        {
            try { await fixture.Categories(db).CreateAsync(new() { Code = "same", Name = "Category" }, Ct); return 201; }
            catch (ApiException error) { return error.StatusCode; }
        }
        Assert.Equal(new[] { 201, 409 }, (await Task.WhenAll(Create(first), Create(second))).Order());
        await using var verify = fixture.Context();
        Assert.Equal(1, await verify.Categories.CountAsync());
    }

    [MasterDataSqlFact]
    public async Task ConcurrentItemUpdatesUsingSameVersionCannotOverwriteWinner()
    {
        await using var fixture = await MasterDataSqlFixture.OpenAsync();
        await using var setup = fixture.Context();
        var category = await fixture.Categories(setup).CreateAsync(new() { Code = "cat", Name = "Category" }, Ct);
        var item = await fixture.Items(setup).CreateAsync(ItemRequest(category.Id, "item", ItemType.RawMaterial), Ct);
        var barrier = new TwoSaves();
        await using var first = fixture.Context(barrier);
        await using var second = fixture.Context(barrier);
        async Task<(int Status, string Name)> Change(BttMiniERP.Api.Data.ApplicationDbContext db, string name)
        {
            try { await fixture.Items(db).UpdateAsync(item.Id, Update(item, name), Ct); return (200, name); }
            catch (ApiException error) { return (error.StatusCode, name); }
        }
        var results = await Task.WhenAll(Change(first, "First"), Change(second, "Second"));
        Assert.Equal(new[] { 200, 409 }, results.Select(x => x.Status).Order());
        await using var verify = fixture.Context();
        Assert.Equal(results.Single(x => x.Status == 200).Name, (await verify.Items.SingleAsync()).Name);
    }

    [MasterDataSqlFact]
    public async Task FailureAfterItemInsertRollsBackItemAndPreservesCategory()
    {
        await using var fixture = await MasterDataSqlFixture.OpenAsync();
        await using var setup = fixture.Context();
        var category = await fixture.Categories(setup).CreateAsync(new() { Code = "cat", Name = "Category" }, Ct);
        var failure = new FailAfterItemSave();
        await using (var db = fixture.Context(failure))
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Items(db).CreateAsync(ItemRequest(category.Id, "rolled-back", ItemType.RawMaterial), Ct));
        Assert.True(failure.SawInsertedItem);
        await using var verify = fixture.Context();
        Assert.Empty(await verify.Items.ToListAsync());
        Assert.Equal(category.Id, (await verify.Categories.SingleAsync()).Id);
    }

    [MasterDataSqlFact]
    public async Task SchemaAndHistoryMatchCategoryItemMigration()
    {
        await using var fixture = await MasterDataSqlFixture.OpenAsync();
        await using var db = fixture.Context();
        await db.Database.OpenConnectionAsync();
        Assert.Equal(3, (await db.Database.GetAppliedMigrationsAsync()).Count());
        var keys = await IdentityDatabaseVerifier.ReadRowsAsync(db, "SELECT t.name,k.name FROM sys.key_constraints k JOIN sys.tables t ON t.object_id=k.parent_object_id WHERE t.name IN ('Items','Categories') AND k.type='PK' ORDER BY t.name");
        Assert.Equal(new[] { "Categories|PK_Categories", "Items|PK_Items" }, keys);
        var fks = await IdentityDatabaseVerifier.ReadRowsAsync(db, "SELECT f.name,f.delete_referential_action_desc,CAST(f.is_disabled AS int),CAST(f.is_not_trusted AS int) FROM sys.foreign_keys f JOIN sys.tables t ON t.object_id=f.parent_object_id WHERE t.name IN ('Items','Categories') ORDER BY f.name");
        Assert.Equal(new[] { "FK_Categories_AspNetUsers_CreatedByUserId|NO_ACTION|0|0", "FK_Categories_AspNetUsers_UpdatedByUserId|NO_ACTION|0|0", "FK_Items_AspNetUsers_CreatedByUserId|NO_ACTION|0|0", "FK_Items_AspNetUsers_UpdatedByUserId|NO_ACTION|0|0", "FK_Items_Categories_CategoryId|NO_ACTION|0|0" }, fks);
        var indexes = await IdentityDatabaseVerifier.ReadRowsAsync(db, "SELECT i.name,CAST(i.is_unique AS int) FROM sys.indexes i JOIN sys.tables t ON t.object_id=i.object_id WHERE t.name IN ('Items','Categories') AND i.is_primary_key=0 ORDER BY i.name");
        Assert.Equal(new[] { "IX_Categories_Code|1", "IX_Categories_CreatedByUserId|0", "IX_Categories_UpdatedByUserId|0", "IX_Items_CategoryId|0", "IX_Items_Code|1", "IX_Items_CreatedByUserId|0", "IX_Items_UpdatedByUserId|0" }, indexes);
        var indexColumns = await IdentityDatabaseVerifier.ReadRowsAsync(db, "SELECT i.name,c.name,ic.key_ordinal,CAST(ic.is_included_column AS int),CAST(i.is_disabled AS int),COALESCE(i.filter_definition,'') FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id IN (OBJECT_ID('dbo.Categories'),OBJECT_ID('dbo.Items')) ORDER BY i.name,ic.key_ordinal");
        Assert.Equal(new[] { "IX_Categories_Code|Code|1|0|0|", "IX_Categories_CreatedByUserId|CreatedByUserId|1|0|0|", "IX_Categories_UpdatedByUserId|UpdatedByUserId|1|0|0|", "IX_Items_CategoryId|CategoryId|1|0|0|", "IX_Items_Code|Code|1|0|0|", "IX_Items_CreatedByUserId|CreatedByUserId|1|0|0|", "IX_Items_UpdatedByUserId|UpdatedByUserId|1|0|0|", "PK_Categories|Id|1|0|0|", "PK_Items|Id|1|0|0|" }, indexColumns);
        var foreignColumns = await IdentityDatabaseVerifier.ReadRowsAsync(db, "SELECT f.name,c.name,OBJECT_NAME(f.referenced_object_id),p.name FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns p ON p.object_id=fc.referenced_object_id AND p.column_id=fc.referenced_column_id WHERE f.parent_object_id IN (OBJECT_ID('dbo.Categories'),OBJECT_ID('dbo.Items')) ORDER BY f.name");
        Assert.Equal(new[] { "FK_Categories_AspNetUsers_CreatedByUserId|CreatedByUserId|AspNetUsers|Id", "FK_Categories_AspNetUsers_UpdatedByUserId|UpdatedByUserId|AspNetUsers|Id", "FK_Items_AspNetUsers_CreatedByUserId|CreatedByUserId|AspNetUsers|Id", "FK_Items_AspNetUsers_UpdatedByUserId|UpdatedByUserId|AspNetUsers|Id", "FK_Items_Categories_CategoryId|CategoryId|Categories|Id" }, foreignColumns);
    }

    private static CreateItemRequest ItemRequest(int categoryId, string code, ItemType type)
        => new() { Code = code, Name = "Item", CategoryId = categoryId, Unit = "kg", ItemType = type, PurchasePrice = 12.34m, SalePrice = 20m };
    private static UpdateItemRequest Update(ItemResponse item, string name, int? categoryId = null)
        => new() { Code = item.Code, Name = name, CategoryId = categoryId ?? item.CategoryId, Unit = item.Unit,
            ItemType = item.ItemType, PurchasePrice = item.PurchasePrice, SalePrice = item.SalePrice, RowVersion = item.RowVersion };
    private static async Task Conflict(Func<Task> action) => Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(action)).StatusCode);

    private sealed class TwoSaves : SaveChangesInterceptor
    {
        private int arrived;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref arrived) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            return result;
        }
    }

    private sealed class FailAfterItemSave : SaveChangesInterceptor
    {
        public bool SawInsertedItem { get; private set; }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken cancellationToken = default)
        {
            SawInsertedItem = await data.Context!.Set<Item>().AsNoTracking().AnyAsync(cancellationToken);
            throw new InvalidOperationException("Test-only failure after SQL insert, before commit.");
        }
    }
}
