using BttMiniERP.Api.Common;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.DTOs.BillOfMaterials;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
using BttMiniERP.Api.Services;
using BttMiniERP.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace BttMiniERP.Tests.Unit.Bom;

public sealed class BillOfMaterialRulesTests
{
    private static CreateBillOfMaterialRequest Request(decimal quantity = 1) => new() { Code = " bom ", FinishedProductId = 1,
        Details = [new() { RawMaterialId = 2, QuantityPerUnit = quantity }] };

    [Theory]
    [InlineData("0")] [InlineData("-1")] [InlineData("0.0001")] [InlineData("1000000000000000")]
    public void InvalidQuantityIsRejected(string quantity)
        => Assert.Equal(400, Assert.Throws<ApiException>(() => BillOfMaterialRules.Details(Request(decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture)))).StatusCode);

    [Fact]
    public void RequiresNonemptyDistinctMaterialsAndRejectsSelfReference()
    {
        foreach (var details in new List<BillOfMaterialDetailRequest>[] { [], null!, [null!],
            [new() { RawMaterialId = 1, QuantityPerUnit = 1 }],
            [new() { RawMaterialId = 2, QuantityPerUnit = 1 }, new() { RawMaterialId = 2, QuantityPerUnit = 2 }],
            [new() { RawMaterialId = 0, QuantityPerUnit = 1 }] })
            Assert.Throws<ApiException>(() => BillOfMaterialRules.Details(new() { Code = "B", FinishedProductId = 1, Details = details }));
        Assert.Equal(InventoryMovement.MaximumQuantity, Assert.Single(BillOfMaterialRules.Details(Request(InventoryMovement.MaximumQuantity))).QuantityPerUnit);
        Assert.Throws<ApiException>(() => BillOfMaterialRules.Details(new() { Code = " ", FinishedProductId = 1, Details = Request().Details }));
    }

    [Theory]
    [InlineData(ItemType.RawMaterial, ItemType.RawMaterial, true, true)]
    [InlineData(ItemType.FinishedProduct, ItemType.FinishedProduct, true, true)]
    [InlineData(ItemType.RawMaterial, ItemType.FinishedProduct, true, false)]
    [InlineData(ItemType.FinishedProduct, ItemType.RawMaterial, true, false)]
    [InlineData(ItemType.RawMaterial, ItemType.RawMaterial, false, false)]
    [InlineData(ItemType.FinishedProduct, ItemType.FinishedProduct, false, false)]
    public void RequiresCorrectActiveType(ItemType actual, ItemType expected, bool active, bool valid)
    {
        var error = Record.Exception(() => BillOfMaterialRules.RequireItem(new() { ItemType = actual, IsActive = active }, expected));
        if (valid) Assert.Null(error); else Assert.Equal(409, Assert.IsType<ApiException>(error).StatusCode);
    }

    [Fact]
    public void VersionAndInactivationGuards()
    {
        Assert.Equal(1, new BillOfMaterial().Version);
        Assert.Equal(2, BillOfMaterialRules.NextVersion(1));
        Assert.Equal(int.MaxValue, BillOfMaterialRules.NextVersion(int.MaxValue - 1));
        foreach (var invalid in new[] { 0, -1, int.MaxValue })
            Assert.Equal(409, Assert.Throws<ApiException>(() => BillOfMaterialRules.NextVersion(invalid)).StatusCode);
        BillOfMaterialRules.RequireActiveForDeactivation(true);
        Assert.Equal(409, Assert.Throws<ApiException>(() => BillOfMaterialRules.RequireActiveForDeactivation(false)).StatusCode);
        Assert.Equal(404, Assert.Throws<ApiException>(() => BillOfMaterialRules.RequireItem(null, ItemType.RawMaterial)).StatusCode);
    }

    [Fact]
    public async Task InvalidInputNeverOpensSqlConnection()
    {
        using var db = new ApplicationDbContextFactory().CreateDbContext(["--offline"]);
        var service = new BillOfMaterialService(db, null!, TimeProvider.System);
        await Assert.ThrowsAsync<ApiException>(() => service.CreateAsync(new(), default));
        await Assert.ThrowsAsync<ApiException>(() => service.UpdateAsync(1, new() { Code = "B", FinishedProductId = 1, Details = Request().Details, RowVersion = "bad" }, default));
        await Assert.ThrowsAsync<ApiException>(() => service.DeactivateAsync(1, "bad", default));
        await Assert.ThrowsAsync<ApiException>(() => service.ListAsync(new() { SortBy = "version" }, default));
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public void ModelMigrationAndSnapshotAreAdditive()
    {
        using var db = new ApplicationDbContextFactory().CreateDbContext(["--offline"]);
        Assert.False(db.Database.HasPendingModelChanges());
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations.Single(x => x.Key.EndsWith("_AddBillOfMaterials", StringComparison.Ordinal)).Value, db.Database.ProviderName!);
        Assert.All(migration.UpOperations, x => Assert.True(x is CreateTableOperation or CreateIndexOperation));
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(new[] { "BillOfMaterials", "BillOfMaterialDetails" }, tables.Select(x => x.Name));
        Assert.Equal(5, tables.Sum(x => x.ForeignKeys.Count));
        Assert.All(tables.SelectMany(x => x.ForeignKeys), x => Assert.Equal(ReferentialAction.NoAction, x.OnDelete));
        Assert.Equal(2, tables.Sum(x => x.CheckConstraints.Count));
        Assert.Equal(3, migration.UpOperations.OfType<CreateIndexOperation>().Count(x => x.IsUnique));
        Assert.Equal(new[] { "BillOfMaterialDetails", "BillOfMaterials" }, migration.DownOperations.Cast<DropTableOperation>().Select(x => x.Name));
        Assert.Equal("decimal(18,3)", db.Model.FindEntityType(typeof(BillOfMaterialDetail))!.FindProperty("QuantityPerUnit")!.GetColumnType());
        Assert.True(db.Model.FindEntityType(typeof(BillOfMaterial))!.FindProperty("RowVersion")!.IsConcurrencyToken);
    }

    [Fact]
    public void BomProfileCannotUseOtherDatabaseOrMissingApproval()
    {
        var values = new Dictionary<string, string> {
            ["BTT_RUN_SQL_TESTS"] = "1", ["BTT_RUN_BOM_SQL_TESTS"] = "1", ["BTT_SQL_TEST_PROFILE"] = "Bom",
            ["BTT_SQL_TEST_ENVIRONMENT"] = "Testing", ["BTT_SQL_APPROVED_SERVER"] = "localhost",
            ["BTT_BOM_SQL_APPROVED_OPERATIONS"] = "Verify,TemporaryWrites,Concurrency,CleanupOwnedRecords",
            ["BTT_SQL_TEST_CONNECTION_STRING"] = "Server=localhost;Database=BttMiniERP_BomTest;Integrated Security=True;Encrypt=True" };
        string? Read(string key) => values.GetValueOrDefault(key);
        Assert.Contains("BttMiniERP_BomTest", MasterDataSqlFixture.ReadSettings(Read, "Bom"));
        foreach (var key in values.Keys.ToArray())
        {
            var old = values[key]; values.Remove(key);
            Assert.Throws<InvalidOperationException>(() => MasterDataSqlFixture.ReadSettings(Read, "Bom")); values[key] = old;
        }
        var original = values["BTT_SQL_TEST_CONNECTION_STRING"];
        foreach (var database in new[] { "master", "BttMiniERP_Dev", "BttMiniERP_Test", "BttMiniERP_PurchaseConfirmationTest" })
        {
            values["BTT_SQL_TEST_CONNECTION_STRING"] = original.Replace("BttMiniERP_BomTest", database);
            Assert.Throws<InvalidOperationException>(() => MasterDataSqlFixture.ReadSettings(Read, "Bom"));
        }
    }
}
