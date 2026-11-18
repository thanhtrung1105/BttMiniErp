using System.Data.Common;
using BttMiniERP.Api.Common;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.DTOs.BillOfMaterials;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
using BttMiniERP.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BttMiniERP.Tests.Integration.Bom;

[Collection("MasterData SQL"), Trait("Category", "SqlServer"), Trait("SqlSuite", "Bom")]
public sealed class BillOfMaterialsSqlTests
{
    private static async Task<CreateBillOfMaterialRequest> Setup(MasterDataSqlFixture f)
    {
        await using var db = f.Context();
        var category = await f.Categories(db).CreateAsync(new() { Code = "BOM", Name = "BOM" }, default);
        async Task<int> Item(string code, ItemType type) => (await f.Items(db).CreateAsync(new()
            { Code = code, Name = code, Unit = "kg", CategoryId = category.Id, ItemType = type }, default)).Id;
        var product = await Item("PRODUCT", ItemType.FinishedProduct);
        var raw = await Item("RAW", ItemType.RawMaterial); var second = await Item("RAW2", ItemType.RawMaterial);
        return new() { Code = " bom ", FinishedProductId = product,
            Details = [new() { RawMaterialId = second, QuantityPerUnit = 2.001m }, new() { RawMaterialId = raw, QuantityPerUnit = 1.125m }] };
    }
    private static UpdateBillOfMaterialRequest Edit(BillOfMaterialResponse x, string code = "EDIT") => new()
        { Code = code, FinishedProductId = x.FinishedProductId, RowVersion = x.RowVersion,
          Details = x.Details.Select(d => new BillOfMaterialDetailRequest { RawMaterialId = d.RawMaterialId, QuantityPerUnit = d.QuantityPerUnit + 1 }).ToList() };
    private static async Task Conflict(Func<Task> action) => Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(action)).StatusCode);
    private static async Task<string[]> Snapshot(MasterDataSqlFixture f)
    {
        await using var db = f.Context(); await db.Database.OpenConnectionAsync();
        var rows = new List<string>();
        foreach (var table in new[] { "BillOfMaterials", "BillOfMaterialDetails", "Items", "InventoryBalances", "InventoryTransactions" })
            rows.Add(string.Concat(await IdentityDatabaseVerifier.ReadRowsAsync(db, $"SELECT * FROM [{table}] ORDER BY 1 FOR JSON PATH, INCLUDE_NULL_VALUES")));
        return rows.ToArray();
    }

    [BomSqlFact]
    public async Task LifecyclePreservesStockIncrementsVersionAndSoftDeletes()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Bom"); var input = await Setup(f);
        BillOfMaterialResponse original;
        await using (var db = f.Context()) original = await f.Boms(db).CreateAsync(input, default);
        Assert.Equal("BOM", original.Code); Assert.Equal(1, original.Version); Assert.Equal(f.ActorId, original.CreatedByUserId);
        Assert.Equal(new[] { 1.125m, 2.001m }, original.Details.Select(x => x.QuantityPerUnit));
        BillOfMaterialResponse updated;
        await using (var db = f.Context()) updated = await f.Boms(db).UpdateAsync(original.Id, Edit(original), default);
        Assert.Equal(2, updated.Version); Assert.NotEqual(original.RowVersion, updated.RowVersion);
        Assert.Equal(original.CreatedAt, updated.CreatedAt); Assert.Equal(f.ActorId, updated.UpdatedByUserId);
        await using (var stale = f.Context()) await Conflict(() => f.Boms(stale).UpdateAsync(original.Id, Edit(original), default));
        await using (var db = f.Context())
        {
            var page = await f.Boms(db).ListAsync(new() { Search = "ed", FinishedProductId = original.FinishedProductId, IsActive = true, PageSize = 1 }, default);
            Assert.Equal(original.Id, Assert.Single(page.Items).Id);
            Assert.Empty((await f.Boms(db).ListAsync(new() { Page = 2, PageSize = 1 }, default)).Items);
        }
        await using (var stale = f.Context()) await Conflict(() => f.Boms(stale).DeactivateAsync(original.Id, original.RowVersion, default));
        BillOfMaterialResponse inactive;
        await using (var db = f.Context()) inactive = await f.Boms(db).DeactivateAsync(updated.Id, updated.RowVersion, default);
        Assert.False(inactive.IsActive); Assert.Equal(3, inactive.Version); Assert.Equal(updated.Details.ToArray(), inactive.Details.ToArray());
        await using (var db = f.Context()) await Conflict(() => f.Boms(db).DeactivateAsync(inactive.Id, inactive.RowVersion, default));
        await using var verify = f.Context();
        Assert.Equal(1, await verify.BillOfMaterials.CountAsync()); Assert.Equal(2, await verify.BillOfMaterialDetails.CountAsync());
        Assert.All(await verify.Items.ToArrayAsync(), x => Assert.True(x.IsActive));
        Assert.All(await verify.InventoryBalances.ToArrayAsync(), x => Assert.Equal(0, x.QuantityOnHand));
        Assert.Empty(await verify.InventoryTransactions.ToArrayAsync());
        Assert.Equal(inactive.RowVersion, (await f.Boms(verify).GetAsync(original.Id, default)).RowVersion);
    }

    [BomSqlFact]
    public async Task FailedReplacementRollsBackHeaderDetailsVersionAndAudit()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Bom"); var input = await Setup(f);
        BillOfMaterialResponse bom; await using (var db = f.Context()) bom = await f.Boms(db).CreateAsync(input, default);
        var before = await Snapshot(f); var fault = new FailSecondSave();
        await using (var db = f.Context(fault)) await Assert.ThrowsAsync<DbUpdateException>(() => f.Boms(db).UpdateAsync(bom.Id, Edit(bom), default));
        Assert.Equal(2, fault.Saves); Assert.Equal(before, await Snapshot(f));
    }

    [BomSqlFact]
    public async Task IndependentConcurrentUpdatesHaveOneWinner()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Bom"); var input = await Setup(f);
        BillOfMaterialResponse bom; await using (var db = f.Context()) bom = await f.Boms(db).CreateAsync(input, default);
        var barrier = new TwoHeaders();
        async Task<(int Status, string Code)> Update(string code)
        {
            await using var db = f.Context(barrier);
            try { await f.Boms(db).UpdateAsync(bom.Id, Edit(bom, code), default); return (200, code); }
            catch (ApiException e) { return (e.StatusCode, code); }
        }
        var results = await Task.WhenAll(Update("ONE"), Update("TWO"));
        Assert.Equal(new[] { 200, 409 }, results.Select(x => x.Status).Order());
        await using var verify = f.Context(); var saved = await f.Boms(verify).GetAsync(bom.Id, default);
        Assert.Equal(2, saved.Version); Assert.Equal(results.Single(x => x.Status == 200).Code, saved.Code);
        Assert.Equal(bom.Details.Select(x => x.QuantityPerUnit + 1), saved.Details.Select(x => x.QuantityPerUnit));
    }

    [BomSqlFact]
    public async Task SqlUniqueForeignKeysChecksAndNoActionProtectSchema()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Bom"); var input = await Setup(f);
        BillOfMaterialResponse bom; await using (var db = f.Context()) bom = await f.Boms(db).CreateAsync(input, default);
        foreach (var kind in new[] { "code", "productUnique", "productFk", "rawFk", "headerFk", "detailUnique", "quantity", "version", "delete" })
        {
            await using var db = f.Context(); await using var tx = await db.Database.BeginTransactionAsync();
            var sql = kind switch
            {
                "code" => "INSERT INTO BillOfMaterials(Code,FinishedProductId,Version,IsActive,CreatedAt,CreatedByUserId) SELECT Code,(SELECT MIN(RawMaterialId) FROM BillOfMaterialDetails),1,1,CreatedAt,CreatedByUserId FROM BillOfMaterials",
                "productUnique" => "INSERT INTO BillOfMaterials(Code,FinishedProductId,Version,IsActive,CreatedAt,CreatedByUserId) SELECT 'OTHER',FinishedProductId,1,1,CreatedAt,CreatedByUserId FROM BillOfMaterials",
                "productFk" => "UPDATE BillOfMaterials SET FinishedProductId=2147483647",
                "rawFk" => "UPDATE BillOfMaterialDetails SET RawMaterialId=2147483647 WHERE Id=(SELECT MIN(Id) FROM BillOfMaterialDetails)",
                "headerFk" => "UPDATE BillOfMaterialDetails SET BillOfMaterialId=2147483647",
                "detailUnique" => "INSERT INTO BillOfMaterialDetails(BillOfMaterialId,RawMaterialId,QuantityPerUnit) SELECT BillOfMaterialId,RawMaterialId,QuantityPerUnit FROM BillOfMaterialDetails",
                "quantity" => "UPDATE BillOfMaterialDetails SET QuantityPerUnit=0",
                "version" => "UPDATE BillOfMaterials SET Version=0",
                _ => "DELETE FROM BillOfMaterials"
            };
            var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql));
            if (kind is "code" or "productUnique" or "detailUnique") Assert.Contains(error.Number, new[] { 2601, 2627 });
            else Assert.Equal(547, error.Number);
            Assert.Contains(kind switch
            {
                "code" => "IX_BillOfMaterials_Code", "productUnique" => "IX_BillOfMaterials_FinishedProductId",
                "productFk" => "FK_BillOfMaterials_Items_FinishedProductId",
                "rawFk" => "FK_BillOfMaterialDetails_Items_RawMaterialId",
                "headerFk" or "delete" => "FK_BillOfMaterialDetails_BillOfMaterials_BillOfMaterialId",
                "detailUnique" => "IX_BillOfMaterialDetails_BillOfMaterialId_RawMaterialId",
                "quantity" => "CK_BillOfMaterialDetails_QuantityPerUnit", _ => "CK_BillOfMaterials_Version"
            }, error.Message);
            await tx.RollbackAsync();
        }
        await using var verify = f.Context();
        Assert.Equal(bom.RowVersion, (await f.Boms(verify).GetAsync(bom.Id, default)).RowVersion);
        Assert.Equal(2, await verify.BillOfMaterialDetails.CountAsync());
        await verify.Database.OpenConnectionAsync();
        var fks = await IdentityDatabaseVerifier.ReadRowsAsync(verify, "SELECT delete_referential_action_desc FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID('BillOfMaterials'),OBJECT_ID('BillOfMaterialDetails'))");
        Assert.Equal(5, fks.Count); Assert.All(fks, x => Assert.Equal("NO_ACTION", x));
        Assert.Equal(9, (await verify.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await verify.Database.GetPendingMigrationsAsync());
    }

    [BomSqlFact]
    public async Task DuplicateCodeRaceMapsConflictAndRollsBackLosingHeader()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Bom"); var input = await Setup(f);
        int secondProduct; int secondRaw;
        await using (var db = f.Context())
        {
            var categoryId = await db.Categories.Select(x => x.Id).SingleAsync();
            secondProduct = (await f.Items(db).CreateAsync(new() { Code = "PRODUCT2", Name = "P2", Unit = "kg", CategoryId = categoryId, ItemType = ItemType.FinishedProduct }, default)).Id;
            secondRaw = (await f.Items(db).CreateAsync(new() { Code = "RAW3", Name = "R3", Unit = "kg", CategoryId = categoryId, ItemType = ItemType.RawMaterial }, default)).Id;
        }
        // Item disjoint: barrier trước Save không tự chặn nhau trên khóa Item.
        var barrier = new TwoCreates();
        async Task<int> Create(CreateBillOfMaterialRequest request)
        {
            await using var db = f.Context(barrier);
            try { await f.Boms(db).CreateAsync(request, default); return 201; }
            catch (ApiException error) { return error.StatusCode; }
        }
        var results = await Task.WhenAll(Create(input), Create(new() { Code = "BOM", FinishedProductId = secondProduct,
            Details = [new() { RawMaterialId = secondRaw, QuantityPerUnit = 1 }] }));
        Assert.Equal(new[] { 201, 409 }, results.Order());
        await using var verify = f.Context();
        var row = await verify.BillOfMaterials.Include(x => x.Details).SingleAsync();
        Assert.Equal(row.FinishedProductId == secondProduct ? 1 : 2, row.Details.Count);
        Assert.Equal(row.Details.Count, await verify.BillOfMaterialDetails.CountAsync());
        Assert.Equal(1, row.Version);
    }

    [BomSqlFact]
    public async Task FailedCreateLeavesNoHeaderOrDetailsAndInactiveProductRemainsUnique()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Bom"); var input = await Setup(f);
        var before = await Snapshot(f);
        await using (var db = f.Context(new FailAfterCreate()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Boms(db).CreateAsync(input, default));
        Assert.Equal(before, await Snapshot(f));
        BillOfMaterialResponse bom;
        await using (var db = f.Context()) bom = await f.Boms(db).CreateAsync(input, default);
        await using (var db = f.Context()) await f.Boms(db).DeactivateAsync(bom.Id, bom.RowVersion, default);
        await using (var db = f.Context()) await Conflict(() => f.Boms(db).CreateAsync(new() { Code = "NEWCODE",
            FinishedProductId = input.FinishedProductId, Details = input.Details }, default));
        await using var verify = f.Context();
        var inactive = await f.Boms(verify).GetAsync(bom.Id, default);
        Assert.False(inactive.IsActive); Assert.Equal(2, inactive.Version);
        var reactivated = await f.Boms(verify).UpdateAsync(bom.Id, Edit(inactive), default);
        Assert.True(reactivated.IsActive); Assert.Equal(3, reactivated.Version);
        Assert.Equal(1, await verify.BillOfMaterials.CountAsync());
    }

    private sealed class FailAfterCreate : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
            => throw new InvalidOperationException("Test-only failure after BOM save before commit.");
    }
    private sealed class TwoCreates : SaveChangesInterceptor
    {
        private int arrivals;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
            return result;
        }
    }

    [BomSqlFact]
    public async Task EligibilityAndItemClassificationAndOverflowAreProtected()
    {
        await using var f = await MasterDataSqlFixture.OpenAsync("Bom"); var input = await Setup(f);
        foreach (var itemId in input.Details.Select(x => x.RawMaterialId).Append(input.FinishedProductId))
        {
            await using (var db = f.Context()) { var item = await db.Items.SingleAsync(x => x.Id == itemId); item.IsActive = false; await db.SaveChangesAsync(); }
            await using (var db = f.Context()) await Conflict(() => f.Boms(db).CreateAsync(input, default));
            await using (var db = f.Context()) { var item = await db.Items.SingleAsync(x => x.Id == itemId); item.IsActive = true; await db.SaveChangesAsync(); }
        }
        BillOfMaterialResponse bom; await using (var db = f.Context()) bom = await f.Boms(db).CreateAsync(input, default);
        foreach (var itemId in input.Details.Select(x => x.RawMaterialId).Append(input.FinishedProductId))
        {
            await using var db = f.Context(); var dto = await f.Items(db).GetAsync(itemId, default);
            await Conflict(() => f.Items(db).UpdateAsync(itemId, new() { Code = dto.Code, Name = dto.Name, Unit = "box",
                CategoryId = dto.CategoryId, ItemType = dto.ItemType, RowVersion = dto.RowVersion }, default));
        }
        await using (var db = f.Context()) { var row = await db.BillOfMaterials.SingleAsync(); row.Version = int.MaxValue; await db.SaveChangesAsync(); }
        BillOfMaterialResponse max; await using (var db = f.Context()) max = await f.Boms(db).GetAsync(bom.Id, default);
        var before = await Snapshot(f);
        await using (var db = f.Context()) await Conflict(() => f.Boms(db).UpdateAsync(bom.Id, Edit(max), default));
        await using (var db = f.Context()) await Conflict(() => f.Boms(db).DeactivateAsync(bom.Id, max.RowVersion, default));
        Assert.Equal(before, await Snapshot(f));
    }

    private sealed class FailSecondSave : SaveChangesInterceptor
    {
        public int Saves;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (++Saves == 2) foreach (var detail in data.Context!.ChangeTracker.Entries<BillOfMaterialDetail>().Where(x => x.State == EntityState.Added)) detail.Entity.QuantityPerUnit = 0;
            return ValueTask.FromResult(result);
        }
    }
    private sealed class TwoHeaders : DbCommandInterceptor
    {
        private int arrivals;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("FROM [BillOfMaterials] WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal))
            { if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult(); await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), ct); }
            return result;
        }
    }
}
