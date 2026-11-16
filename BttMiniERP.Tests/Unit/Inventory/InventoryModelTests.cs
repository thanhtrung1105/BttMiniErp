using BttMiniERP.Api.Data;
using BttMiniERP.Api.Data.Migrations;
using BttMiniERP.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace BttMiniERP.Tests.Unit.Inventory;

public sealed class InventoryModelTests
{
    [Fact]
    public void MappingAndMigrationAreAdditiveWithZeroBackfill()
    {
        using var db = new ApplicationDbContextFactory().CreateDbContext(["--offline"]);
        Assert.False(db.Database.HasPendingModelChanges());
        var balance = db.Model.FindEntityType(typeof(InventoryBalance))!;
        Assert.Equal("ItemId", Assert.Single(balance.FindPrimaryKey()!.Properties).Name);
        Assert.True(balance.FindProperty("RowVersion")!.IsConcurrencyToken);
        Assert.Equal("decimal(18,3)", balance.FindProperty("QuantityOnHand")!.GetColumnType());
        var ledger = db.Model.FindEntityType(typeof(InventoryTransaction))!;
        Assert.Equal("decimal(18,3)", ledger.FindProperty("QuantityDelta")!.GetColumnType());
        Assert.Equal("decimal(18,3)", ledger.FindProperty("BalanceAfter")!.GetColumnType());
        Assert.Contains(ledger.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(new[] { "SourceDocumentType", "SourceDocumentId", "SourceLineId", "TransactionType" }));
        var migration = new AddInventoryLedger();
        Assert.All(migration.UpOperations, op => Assert.True(op is CreateTableOperation or CreateIndexOperation or SqlOperation));
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(new[] { "InventoryBalances", "InventoryTransactions" }, tables.Select(x => x.Name));
        Assert.Equal(5, tables.Sum(x => x.ForeignKeys.Count));
        Assert.All(tables.SelectMany(x => x.ForeignKeys), fk => Assert.Equal(ReferentialAction.NoAction, fk.OnDelete));
        Assert.Equal(4, tables.Sum(x => x.CheckConstraints.Count));
        var sql = Assert.Single(migration.UpOperations.OfType<SqlOperation>());
        Assert.Contains("SELECT i.[Id], 0, i.[CreatedAt], i.[CreatedByUserId]", sql.Sql);
        Assert.DoesNotContain("InventoryTransactions", sql.Sql);
        Assert.False(sql.SuppressTransaction);
        var script = db.GetService<IMigrator>().GenerateScript("AddPartners", "AddInventoryLedger");
        Assert.DoesNotContain("ALTER TABLE", script);
        Assert.Contains("BEGIN TRANSACTION", script);
        Assert.Equal(new[] { "InventoryBalances", "InventoryTransactions" }, migration.DownOperations.Cast<DropTableOperation>().Select(x => x.Name));
    }
}
