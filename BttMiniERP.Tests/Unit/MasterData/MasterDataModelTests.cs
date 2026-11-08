using BttMiniERP.Api.Data;
using BttMiniERP.Api.Data.Migrations;
using BttMiniERP.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace BttMiniERP.Tests.Unit.MasterData;

public sealed class MasterDataModelTests
{
    [Fact]
    public void ModelHasRequiredKeysAuditPrecisionAndConcurrency()
    {
        using var context = new ApplicationDbContextFactory().CreateDbContext(["--offline"]);
        foreach (var type in new[] { typeof(Category), typeof(Item) })
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Equal(typeof(int), entity.FindPrimaryKey()!.Properties.Single().ClrType);
            Assert.Equal(50, entity.FindProperty("Code")!.GetMaxLength());
            Assert.Equal(200, entity.FindProperty("Name")!.GetMaxLength());
            Assert.Contains(entity.GetIndexes(), x => x.IsUnique && x.Properties.Single().Name == "Code");
            Assert.True(entity.FindProperty("RowVersion")!.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.OnAddOrUpdate, entity.FindProperty("RowVersion")!.ValueGenerated);
            Assert.Equal("datetimeoffset", entity.FindProperty("CreatedAt")!.GetColumnType());
            Assert.False(entity.FindProperty("CreatedByUserId")!.IsNullable);
            Assert.Equal(128, entity.FindProperty("CreatedByUserId")!.GetMaxLength());
            Assert.True(entity.FindProperty("UpdatedByUserId")!.IsNullable);
            Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior));
        }
        var item = context.Model.FindEntityType(typeof(Item))!;
        Assert.Equal(20, item.FindProperty("Unit")!.GetMaxLength());
        Assert.False(item.FindProperty("CategoryId")!.IsNullable);
        foreach (var price in new[] { "PurchasePrice", "SalePrice" })
            Assert.Equal("decimal(18,2)", item.FindProperty(price)!.GetColumnType());
        Assert.DoesNotContain(context.Model.GetEntityTypes(), x => x.GetTableName()!.Contains("Inventory"));
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public void MigrationIsAdditiveAndSnapshotMatchesWithoutChangingIdentity()
    {
        using var context = new ApplicationDbContextFactory().CreateDbContext(["--offline"]);
        Assert.False(context.Database.HasPendingModelChanges());
        var migration = new AddCategoriesAndItems();
        Assert.All(migration.UpOperations, op => Assert.True(op is CreateTableOperation or CreateIndexOperation));
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(new[] { "Categories", "Items" }, tables.Select(x => x.Name));
        Assert.Equal(5, tables.Sum(x => x.ForeignKeys.Count));
        Assert.All(tables.SelectMany(x => x.ForeignKeys), fk => Assert.Equal(ReferentialAction.NoAction, fk.OnDelete));
        Assert.Equal(3, tables.Single(x => x.Name == "Items").CheckConstraints.Count);
        Assert.Equal(7, migration.UpOperations.OfType<CreateIndexOperation>().Count());
        Assert.Equal(new[] { "Items", "Categories" }, migration.DownOperations.Cast<DropTableOperation>().Select(x => x.Name));
        var script = context.GetService<IMigrator>().GenerateScript("LimitIdentityKeyLengths", "AddCategoriesAndItems");
        Assert.DoesNotContain("ALTER TABLE [AspNet", script);
        Assert.DoesNotContain("DROP TABLE", script);
        Assert.DoesNotContain("INSERT INTO [AspNet", script);
        Assert.Contains("BEGIN TRANSACTION", script);
    }
}
