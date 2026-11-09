using BttMiniERP.Api.Data;
using BttMiniERP.Api.Data.Migrations;
using BttMiniERP.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace BttMiniERP.Tests.Unit.Partners;

public sealed class PartnerModelTests
{
    [Fact]
    public void ModelAndAdditiveMigrationPreserveExistingTables()
    {
        using var db = new ApplicationDbContextFactory().CreateDbContext(["--offline"]);
        Assert.False(db.Database.HasPendingModelChanges());
        foreach (var type in new[] { typeof(Supplier), typeof(Customer) })
        {
            var entity = db.Model.FindEntityType(type)!;
            Assert.Equal(typeof(int), entity.FindPrimaryKey()!.Properties.Single().ClrType);
            foreach (var (field, length) in new[] { ("Code", 50), ("Name", 200), ("Phone", 30), ("Email", 254), ("Address", 500), ("CreatedByUserId", 128) })
                Assert.Equal(length, entity.FindProperty(field)!.GetMaxLength());
            Assert.Contains(entity.GetIndexes(), x => x.IsUnique && x.Properties.Single().Name == "Code");
            Assert.True(entity.FindProperty("RowVersion")!.IsConcurrencyToken);
            Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior));
        }
        var migration = new AddPartners();
        Assert.All(migration.UpOperations, op => Assert.True(op is CreateTableOperation or CreateIndexOperation));
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(new[] { "Customers", "Suppliers" }, tables.Select(x => x.Name));
        Assert.Equal(4, tables.Sum(x => x.ForeignKeys.Count));
        Assert.All(tables.SelectMany(x => x.ForeignKeys), fk => Assert.Equal(ReferentialAction.NoAction, fk.OnDelete));
        Assert.Equal(8, migration.UpOperations.OfType<CreateIndexOperation>().Count());
        Assert.Equal(new[] { "Customers", "Suppliers" }, migration.DownOperations.Cast<DropTableOperation>().Select(x => x.Name));
        var script = db.GetService<IMigrator>().GenerateScript("AddCategoriesAndItems", "AddPartners");
        Assert.DoesNotContain("ALTER TABLE", script);
        Assert.DoesNotContain("DROP TABLE", script);
        Assert.Contains("BEGIN TRANSACTION", script);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }
}
