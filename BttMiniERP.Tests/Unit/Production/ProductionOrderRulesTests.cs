using BttMiniERP.Api.Common;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
using BttMiniERP.Api.Services;
using BttMiniERP.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace BttMiniERP.Tests.Unit.Production;

public sealed class ProductionOrderRulesTests
{
    [Theory]
    [InlineData("0.333", "1.001", "0.334")]
    [InlineData("2", "3", "6")]
    [InlineData("2", "4", "8")]
    [InlineData("0.001", "0.001", "0.001")]
    [InlineData("999999999999999.999", "1", "999999999999999.999")]
    public void DemandRoundsUpWithoutLosingMaterial(string perUnit, string quantity, string expected)
        => Assert.Equal(Parse(expected), ProductionOrderRules.RequiredQuantity(Parse(perUnit), Parse(quantity)));
    private static decimal Parse(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("0")] [InlineData("-1")] [InlineData("0.0001")] [InlineData("1000000000000000")]
    public void InvalidOutputQuantityIsRejected(string value)
        => Assert.Equal(400, Assert.Throws<ApiException>(() => ProductionOrderRules.RequireQuantity(Parse(value))).StatusCode);

    [Theory]
    [InlineData("999999999999999.999", "2")]
    [InlineData("999999999999999.999", "999999999999999.999")]
    public void DemandOverflowIsAValidationError(string perUnit, string quantity)
        => Assert.Equal(400, Assert.Throws<ApiException>(() => ProductionOrderRules.RequiredQuantity(Parse(perUnit), Parse(quantity))).StatusCode);

    [Theory]
    [InlineData("0")] [InlineData("-1")] [InlineData("0.0001")] [InlineData("1000000000000000")]
    public void InvalidStoredPerUnitIsConflict(string value)
        => Assert.Equal(409, Assert.Throws<ApiException>(() => ProductionOrderRules.RequiredQuantity(Parse(value), 1)).StatusCode);

    private static BillOfMaterial Bom() => new() { Id = 1, FinishedProductId = 10, Version = 2,
        Details = [new() { RawMaterialId = 2, QuantityPerUnit = 2 }, new() { RawMaterialId = 3, QuantityPerUnit = 0.333m }] };

    [Fact]
    public void SnapshotCopiesValuesAndIsIndependentOfLaterBomMutations()
    {
        var bom = Bom(); var snapshot = ProductionOrderRules.Snapshot(bom, 3);
        bom.Details[0].QuantityPerUnit = 4; bom.Details.Clear(); bom.IsActive = false;
        Assert.Equal(2, snapshot.Length); Assert.Equal(2, snapshot[0].QuantityPerUnitSnapshot);
        Assert.Equal(6, snapshot[0].RequiredQuantity); Assert.All(snapshot, x => Assert.Null(x.ActualQuantity));
        Assert.Equal(8, ProductionOrderRules.RequiredQuantity(snapshot[0].QuantityPerUnitSnapshot, 4));
    }

    [Fact]
    public void MissingInactiveEmptyDuplicateAndSelfReferencingBomsAreRejected()
    {
        Assert.Equal(404, Assert.Throws<ApiException>(() => ProductionOrderRules.Snapshot(null, 1)).StatusCode);
        foreach (var kind in new[] { "inactive", "empty", "duplicate", "self", "version" })
        {
            var bom = Bom();
            switch (kind) {
                case "inactive": bom.IsActive = false; break;
                case "empty": bom.Details.Clear(); break;
                case "duplicate": bom.Details[1].RawMaterialId = 2; break;
                case "self": bom.Details[1].RawMaterialId = 10; break;
                default: bom.Version = 0; break;
            }
            Assert.Equal(409, Assert.Throws<ApiException>(() => ProductionOrderRules.Snapshot(bom, 1)).StatusCode);
        }
    }

    [Theory] [InlineData(DocumentStatus.Confirmed)] [InlineData(DocumentStatus.Cancelled)] [InlineData((DocumentStatus)99)]
    public void TerminalOrUnknownStatusCannotBeEditedOrCancelled(DocumentStatus status)
        => Assert.Equal(409, Assert.Throws<ApiException>(() => ProductionOrderRules.RequireDraft(status)).StatusCode);

    [Fact]
    public async Task InvalidRequestsFailBeforeAnySqlConnection()
    {
        using var db = new ApplicationDbContextFactory().CreateDbContext(["--offline"]);
        var service = new ProductionOrderService(db, null!, TimeProvider.System, null!);
        await Assert.ThrowsAsync<ApiException>(() => service.CreateAsync(new() { BomId = 1, Quantity = 0 }, default));
        await Assert.ThrowsAsync<ApiException>(() => service.CreateAsync(new() { BomId = 0, Quantity = 1 }, default));
        await Assert.ThrowsAsync<ApiException>(() => service.UpdateAsync(1, new() { Quantity = 1, RowVersion = "bad" }, default));
        await Assert.ThrowsAsync<ApiException>(() => service.CancelAsync(1, "bad", default));
        await Assert.ThrowsAsync<ApiException>(() => service.ListAsync(new() { PageSize = 101 }, default));
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public void ProductionSchemaIsAdditiveAndMatchesSnapshot()
    {
        using var db = new ApplicationDbContextFactory().CreateDbContext(["--offline"]);
        Assert.False(db.Database.HasPendingModelChanges());
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations.Single(x => x.Key.EndsWith("_AddProductionOrders", StringComparison.Ordinal)).Value, db.Database.ProviderName!);
        Assert.All(migration.UpOperations, x => Assert.True(x is CreateTableOperation or CreateIndexOperation));
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(new[] { "ProductionOrders", "ProductionOrderMaterials" }, tables.Select(x => x.Name));
        Assert.Equal(8, tables.Sum(x => x.ForeignKeys.Count));
        Assert.All(tables.SelectMany(x => x.ForeignKeys), x => Assert.Equal(ReferentialAction.NoAction, x.OnDelete));
        Assert.Equal(6, tables.Sum(x => x.CheckConstraints.Count));
        Assert.Equal(2, migration.UpOperations.OfType<CreateIndexOperation>().Count(x => x.IsUnique));
        Assert.Equal(new[] { "ProductionOrderMaterials", "ProductionOrders" }, migration.DownOperations.Cast<DropTableOperation>().Select(x => x.Name));
        var detail = db.Model.FindEntityType(typeof(ProductionOrderMaterial))!;
        foreach (var name in new[] { "QuantityPerUnitSnapshot", "RequiredQuantity", "ActualQuantity" })
            Assert.Equal("decimal(18,3)", detail.FindProperty(name)!.GetColumnType());
        Assert.True(detail.FindProperty("ActualQuantity")!.IsNullable);
        Assert.True(db.Model.FindEntityType(typeof(ProductionOrder))!.FindProperty("RowVersion")!.IsConcurrencyToken);
        var number = DocumentNumberGenerator.NewProductionOrder();
        Assert.Matches("^PO-[0-9A-F]{32}$", number);
        Assert.NotEqual(number, DocumentNumberGenerator.NewProductionOrder());
    }

    [Fact]
    public void ProductionProfileRequiresAllApprovalsAndExactDatabase()
    {
        var values = new Dictionary<string,string> {
            ["BTT_RUN_SQL_TESTS"]="1", ["BTT_RUN_PRODUCTION_SQL_TESTS"]="1", ["BTT_SQL_TEST_PROFILE"]="Production",
            ["BTT_SQL_TEST_ENVIRONMENT"]="Testing", ["BTT_SQL_APPROVED_SERVER"]="localhost",
            ["BTT_PRODUCTION_SQL_APPROVED_OPERATIONS"]="Verify,TemporaryWrites,Concurrency,CleanupOwnedRecords",
            ["BTT_SQL_TEST_CONNECTION_STRING"]="Server=localhost;Database=BttMiniERP_ProductionTest;Integrated Security=True;Encrypt=True" };
        string? Read(string key) => values.GetValueOrDefault(key);
        Assert.Contains("BttMiniERP_ProductionTest", MasterDataSqlFixture.ReadSettings(Read, "Production"));
        foreach (var key in values.Keys.ToArray()) {
            var old=values[key]; values.Remove(key); Assert.Throws<InvalidOperationException>(() => MasterDataSqlFixture.ReadSettings(Read,"Production")); values[key]=old;
        }
        var connection=values["BTT_SQL_TEST_CONNECTION_STRING"];
        foreach (var target in new[] { "master", "BttMiniERP_Dev", "BttMiniERP_Test", "BttMiniERP_BomTest" }) {
            values["BTT_SQL_TEST_CONNECTION_STRING"]=connection.Replace("BttMiniERP_ProductionTest",target);
            Assert.Throws<InvalidOperationException>(() => MasterDataSqlFixture.ReadSettings(Read,"Production"));
        }
    }
}
