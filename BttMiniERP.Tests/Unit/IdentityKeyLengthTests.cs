using BttMiniERP.Api.Data;
using BttMiniERP.Api.Data.Configurations;
using BttMiniERP.Api.Data.Migrations;
using BttMiniERP.Tests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BttMiniERP.Tests.Unit;

public sealed class IdentityKeyLengthTests
{
    [Fact]
    public void RuntimeAndDesignTimeModelsUseSameKeyLengthsAndOptions()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=127.0.0.1,1;Database=BttMiniERP_Dev;Integrated Security=True"
        }).Build();
        var services = new ServiceCollection();
        services.AddPersistence(configuration, "Development");
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var runtime = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        using var design = new ApplicationDbContextFactory().CreateDbContext(["--offline"]);
        var options = provider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        Assert.Equal(128, options.Stores.MaxLengthForKeys);
        Assert.Equal(IdentitySchemaVersions.Version1, options.Stores.SchemaVersion);
        var designOptions = design.GetService<IDbContextOptions>().Extensions.OfType<CoreOptionsExtension>()
            .Single().ApplicationServiceProvider!.GetRequiredService<IOptions<IdentityOptions>>().Value;
        Assert.Equal(options.Stores.MaxLengthForKeys, designOptions.Stores.MaxLengthForKeys);
        Assert.Equal(options.Stores.SchemaVersion, designOptions.Stores.SchemaVersion);
        Assert.Equal(KeyShape(design), KeyShape(runtime));
        Assert.Equal(12, KeyShape(runtime).Length);
        Assert.All(KeyProperties(runtime), property =>
        {
            Assert.Equal(128, property.GetMaxLength());
            Assert.Equal("nvarchar(128)", property.GetColumnType());
        });
        foreach (var entity in runtime.Model.GetEntityTypes())
        {
            var key = entity.FindPrimaryKey()!;
            if (key.Properties.All(property => property.ClrType == typeof(string)))
                Assert.InRange(key.Properties.Sum(property => 2 * property.GetMaxLength()!.Value), 1, 900);
            foreach (var foreignKey in entity.GetForeignKeys())
            {
                Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior);
                Assert.Equal(foreignKey.PrincipalKey.Properties.Single().GetMaxLength(), foreignKey.Properties.Single().GetMaxLength());
                Assert.Equal(foreignKey.PrincipalKey.Properties.Single().GetColumnType(), foreignKey.Properties.Single().GetColumnType());
            }
        }
    }

    [Fact]
    public void UpgradeGuardsDataBeforeAlteringAndRestoresConstraintsWithoutDeletingRows()
    {
        var migration = new LimitIdentityKeyLengths();
        var operations = migration.UpOperations;
        var guards = operations.TakeWhile(op => op is SqlOperation).Cast<SqlOperation>().ToArray();
        var guardSql = string.Join("\n", guards.Select(op => op.Sql));
        Assert.Contains("DATALENGTH", guardSql);
        Assert.Contains("> 256", guardSql);
        Assert.Contains("THROW", guardSql);
        Assert.Contains("TABLOCKX, HOLDLOCK", guardSql);
        Assert.All(guards, guard => Assert.False(guard.SuppressTransaction));
        Assert.Equal(12, operations.OfType<AlterColumnOperation>().Count());
        Assert.All(operations.OfType<AlterColumnOperation>(), column => Assert.Equal(128, column.MaxLength));
        Assert.Equal(5, operations.OfType<DropPrimaryKeyOperation>().Count());
        Assert.Equal(5, operations.OfType<AddPrimaryKeyOperation>().Count());
        Assert.Equal(6, operations.OfType<DropForeignKeyOperation>().Count());
        Assert.Equal(6, operations.OfType<AddForeignKeyOperation>().Count());
        Assert.DoesNotContain(operations, op => op is DropTableOperation or DropColumnOperation or DeleteDataOperation or UpdateDataOperation or InsertDataOperation);

        using var context = new ApplicationDbContextFactory().CreateDbContext(["--offline"]);
        var script = context.GetService<IMigrator>().GenerateScript("CreateIdentitySchema", "LimitIdentityKeyLengths");
        Assert.True(script.IndexOf("DATALENGTH", StringComparison.Ordinal) < script.IndexOf("DROP CONSTRAINT", StringComparison.Ordinal));
        Assert.DoesNotContain("DROP TABLE", script);
        Assert.DoesNotContain("DELETE FROM", script);
        Assert.DoesNotContain("UPDATE [AspNet", script);
        Assert.DoesNotContain("INSERT INTO [AspNet", script);
        Assert.Equal(12, System.Text.RegularExpressions.Regex.Matches(script, "ALTER COLUMN").Count);
        Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(script, "ADD CONSTRAINT \\[PK_").Count);
        Assert.Equal(6, System.Text.RegularExpressions.Regex.Matches(script, "ADD CONSTRAINT \\[FK_").Count);
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void PreviousSqlApprovalCannotApplyNewMigration()
    {
        var pending = new[] { "20261103140045_LimitIdentityKeyLengths" };
        Assert.Throws<InvalidOperationException>(() => SqlServerFixture.ValidateApprovedMigrations(pending, null));
        Assert.Throws<InvalidOperationException>(() => SqlServerFixture.ValidateApprovedMigrations(pending, "20261103102340_CreateIdentitySchema"));
        SqlServerFixture.ValidateApprovedMigrations(pending, pending[0]);
    }

    [Fact]
    public void BothDirectionsPreserveOriginalForeignKeysPrimaryKeysAndIndexes()
    {
        var original = new CreateIdentitySchema();
        var tables = original.UpOperations.OfType<CreateTableOperation>().ToArray();
        var originalForeignKeys = tables.SelectMany(table => table.ForeignKeys).OrderBy(fk => fk.Name).ToArray();
        using var context = new ApplicationDbContextFactory().CreateDbContext(["--offline"]);
        var currentForeignKeys = context.Model.GetEntityTypes().Where(entity => entity.GetTableName()!.StartsWith("AspNet"))
            .SelectMany(entity => entity.GetForeignKeys())
            .OrderBy(fk => fk.GetConstraintName()).ToArray();
        Assert.Equal(6, originalForeignKeys.Length);
        Assert.Equal(originalForeignKeys.Select(fk => fk.Name), currentForeignKeys.Select(fk => fk.GetConstraintName()));
        Assert.All(originalForeignKeys, fk => Assert.Equal(ReferentialAction.NoAction, fk.OnDelete));
        Assert.All(currentForeignKeys, fk => Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior));

        foreach (var operations in new[] { new LimitIdentityKeyLengths().UpOperations, new LimitIdentityKeyLengths().DownOperations })
        {
            var recreated = operations.OfType<AddForeignKeyOperation>().OrderBy(fk => fk.Name).ToArray();
            Assert.Equal(originalForeignKeys.Select(ForeignKeyShape), recreated.Select(ForeignKeyShape));
            foreach (var key in operations.OfType<AddPrimaryKeyOperation>())
            {
                var baseline = tables.Single(table => table.Name == key.Table).PrimaryKey!;
                Assert.Equal(baseline.Name, key.Name);
                Assert.Equal(baseline.Columns, key.Columns);
            }
            Assert.DoesNotContain(operations, op => op is DropTableOperation or DropColumnOperation or DeleteDataOperation or UpdateDataOperation);
        }

        var migrator = context.GetService<IMigrator>();
        foreach (var script in new[] {
            migrator.GenerateScript("CreateIdentitySchema", "LimitIdentityKeyLengths"),
            migrator.GenerateScript("LimitIdentityKeyLengths", "CreateIdentitySchema") })
        {
            Assert.Contains("BEGIN TRANSACTION;", script);
            Assert.Contains("COMMIT;", script);
            Assert.Contains("SET QUOTED_IDENTIFIER ON;", script);
            Assert.Contains("SET XACT_ABORT ON;", script);
            Assert.DoesNotContain("BttMiniERP_Dev", script);
            Assert.DoesNotContain("BttMiniERP_Test", script);
            Assert.True(script.IndexOf("Unexpected Identity column schema", StringComparison.Ordinal) < script.IndexOf("DROP CONSTRAINT", StringComparison.Ordinal));
            Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(script, "DROP INDEX").Count);
            foreach (var index in original.UpOperations.OfType<CreateIndexOperation>())
            {
                if (index.Name.StartsWith("IX_", StringComparison.Ordinal))
                    Assert.Contains($"CREATE INDEX [{index.Name}] ON [{index.Table}] ([{index.Columns.Single()}]);", script);
                else
                    Assert.DoesNotContain($"DROP INDEX [{index.Name}]", script);
            }
        }
        foreach (var column in new LimitIdentityKeyLengths().DownOperations.OfType<AlterColumnOperation>())
        {
            var baseline = tables.Single(table => table.Name == column.Table).Columns.Single(c => c.Name == column.Name);
            Assert.Equal(baseline.ColumnType, column.ColumnType);
            Assert.Equal(baseline.MaxLength, column.MaxLength);
            Assert.Equal(baseline.IsNullable, column.IsNullable);
        }
        // Tạo schema từ đầu: baseline được áp dụng/ghi history trước bản sửa, không khóa tên database.
        var cleanScript = migrator.GenerateScript("0", "LimitIdentityKeyLengths");
        Assert.True(cleanScript.IndexOf("VALUES (N'20261103102340_CreateIdentitySchema'", StringComparison.Ordinal)
            < cleanScript.IndexOf("Unexpected migration baseline", StringComparison.Ordinal));
    }

    private static string ForeignKeyShape(AddForeignKeyOperation fk)
        => $"{fk.Name}|{fk.Table}|{string.Join(',', fk.Columns)}|{fk.PrincipalTable}|{string.Join(',', fk.PrincipalColumns!)}|{fk.OnDelete}|{fk.OnUpdate}";

    private static IEnumerable<Microsoft.EntityFrameworkCore.Metadata.IProperty> KeyProperties(ApplicationDbContext context)
        => context.Model.GetEntityTypes().Where(entity => entity.GetTableName()!.StartsWith("AspNet"))
            .SelectMany(entity => entity.GetProperties())
            .Where(property => property.ClrType == typeof(string) && (property.IsPrimaryKey() || property.IsForeignKey()));

    private static string[] KeyShape(ApplicationDbContext context)
        => KeyProperties(context).Select(property => $"{property.DeclaringType.Name}.{property.Name}:{property.GetColumnType()}:{property.GetMaxLength()}").Order().ToArray();
}
