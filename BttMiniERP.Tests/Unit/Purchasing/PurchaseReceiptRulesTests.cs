using System.Globalization;
using BttMiniERP.Api.Common;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.Data.Migrations;
using BttMiniERP.Api.DTOs.PurchaseReceipts;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
using BttMiniERP.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
namespace BttMiniERP.Tests.Unit.Purchasing;

public sealed class PurchaseReceiptRulesTests
{
    [Theory]
    [InlineData("0", "1")][InlineData("-1", "1")][InlineData("1.0001", "1")]
    [InlineData("1", "-1")][InlineData("1", "1.001")][InlineData("1000000000000000", "1")]
    [InlineData("999999999999999.999", "9999999999999999.99")]
    [InlineData("2", "9999999999999999.99")]
    public void RejectsInvalidPrecisionOrOverflow(string quantity, string price)
    {
        Assert.Equal(400, Assert.Throws<ApiException>(() => PurchaseReceiptRules.Calculate(new() { SupplierId = 1, Lines = [new() {
            ItemId = 1, Quantity = decimal.Parse(quantity, CultureInfo.InvariantCulture), UnitPrice = decimal.Parse(price, CultureInfo.InvariantCulture) }] })).StatusCode);
    }
    [Fact]
    public void EmptyDraftRoundingDuplicateNullAndTotalOverflow()
    {
        Assert.Empty(PurchaseReceiptRules.Calculate(new() { SupplierId = 1 }));
        var lines = PurchaseReceiptRules.Calculate(new() { SupplierId = 1, Lines = [new() { ItemId = 1, Quantity = .125m, UnitPrice = .20m }, new() { ItemId = 2, Quantity = 1, UnitPrice = 0 }] });
        Assert.Equal(.03m, lines.Sum(x => x.LineTotal)); Assert.Equal(new[] { 1, 2 }, lines.Select(x => x.LineNumber));
        foreach (var invalid in new[] {
            new CreatePurchaseReceiptRequest { SupplierId = 0 }, new CreatePurchaseReceiptRequest { SupplierId = 1, Lines = null! },
            new CreatePurchaseReceiptRequest { SupplierId = 1, Lines = [null!] },
            new CreatePurchaseReceiptRequest { SupplierId = 1, Lines = [new() { ItemId = 1, Quantity = 1 },new() { ItemId = 1, Quantity = 1 }] },
            new CreatePurchaseReceiptRequest { SupplierId = 1, Lines = [new() { ItemId = 1, Quantity = 1, UnitPrice = PurchaseReceiptRules.MaximumMoney },new() { ItemId = 2, Quantity = 1, UnitPrice = 1 }] } })
            Assert.Throws<ApiException>(() => PurchaseReceiptRules.Calculate(invalid));
    }
    [Theory]
    [InlineData(ItemType.RawMaterial, true, true)][InlineData(ItemType.RawMaterial, false, false)]
    [InlineData(ItemType.FinishedProduct, true, false)][InlineData((ItemType)99, true, false)]
    public void OnlyActiveRawMaterials(ItemType type, bool active, bool valid)
    {
        var error = Record.Exception(() => PurchaseReceiptRules.RequireMaterial(new Item { ItemType = type, IsActive = active }));
        if (valid) Assert.Null(error); else Assert.Equal(409, Assert.IsType<ApiException>(error).StatusCode);
    }
    [Fact]
    public void SupplierAndDraftGuards()
    {
        PurchaseReceiptRules.RequireSupplier(new Supplier());
        Assert.Equal(404, Assert.Throws<ApiException>(() => PurchaseReceiptRules.RequireSupplier(null)).StatusCode);
        Assert.Equal(404, Assert.Throws<ApiException>(() => PurchaseReceiptRules.RequireMaterial(null)).StatusCode);
        Assert.Equal(409, Assert.Throws<ApiException>(() => PurchaseReceiptRules.RequireSupplier(new() { IsActive = false })).StatusCode);
        PurchaseReceiptRules.RequireDraft(DocumentStatus.Draft);
        foreach (var state in new[] { DocumentStatus.Cancelled, DocumentStatus.Confirmed, (DocumentStatus)99 })
            Assert.Equal(409, Assert.Throws<ApiException>(() => PurchaseReceiptRules.RequireDraft(state)).StatusCode);
    }
    [Fact]
    public void GeneratorUsesApprovedFormatAndIndependentCalls()
    {
        var numbers = Enumerable.Range(0, 2000).AsParallel().Select(_ => DocumentNumberGenerator.NewPurchaseReceipt()).ToArray();
        Assert.All(numbers, n => Assert.Matches("^PR-[A-F0-9]{32}$", n)); Assert.Equal(numbers.Length, numbers.Distinct().Count());
    }
    [Fact]
    public async Task InvalidServiceInputDoesNotConnectOrTrackWrites()
    {
        using var db = new ApplicationDbContextFactory().CreateDbContext(["--offline"]);
        var service = new PurchaseReceiptService(db, null!, TimeProvider.System, null!);
        await Assert.ThrowsAsync<ApiException>(() => service.CreateAsync(new() { SupplierId = 0 }, default));
        await Assert.ThrowsAsync<ApiException>(() => service.ListAsync(new() { PageSize = 101 }, default));
        await Assert.ThrowsAsync<ApiException>(() => service.UpdateAsync(1, new() { SupplierId = 1, RowVersion = "bad" }, default));
        await Assert.ThrowsAsync<ApiException>(() => service.CancelAsync(1, "bad", default));
        Assert.Empty(db.ChangeTracker.Entries()); Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }
    [Fact]
    public void MappingMigrationAndSnapshotMatchAndDoNotTouchInventory()
    {
        using var db = new ApplicationDbContextFactory().CreateDbContext(["--offline"]);
        Assert.False(db.Database.HasPendingModelChanges());
        var header = db.Model.FindEntityType(typeof(PurchaseReceipt))!;
        Assert.True(header.FindProperty("RowVersion")!.IsConcurrencyToken); Assert.Equal(40, header.FindProperty("ReceiptNumber")!.GetMaxLength());
        Assert.Equal("decimal(18,2)", header.FindProperty("TotalAmount")!.GetColumnType());
        var line = db.Model.FindEntityType(typeof(PurchaseReceiptLine))!;
        Assert.Equal("decimal(18,3)", line.FindProperty("Quantity")!.GetColumnType());
        Assert.Equal("decimal(18,2)", line.FindProperty("UnitPrice")!.GetColumnType());
        var migration = new AddPurchaseReceipts();
        Assert.All(migration.UpOperations, x => Assert.True(x is CreateTableOperation or CreateIndexOperation));
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(new[] { "PurchaseReceipts", "PurchaseReceiptLines" }, tables.Select(x => x.Name));
        Assert.Equal(6, tables.Sum(x => x.ForeignKeys.Count)); Assert.Equal(6, tables.Sum(x => x.CheckConstraints.Count));
        Assert.All(tables.SelectMany(x => x.ForeignKeys), x => Assert.Equal(ReferentialAction.NoAction, x.OnDelete));
        Assert.Equal(3, migration.UpOperations.OfType<CreateIndexOperation>().Count(x => x.IsUnique));
    }
}
