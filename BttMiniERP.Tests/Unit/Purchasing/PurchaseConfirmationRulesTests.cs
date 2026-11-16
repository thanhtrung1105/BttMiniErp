using BttMiniERP.Api.Common;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
using BttMiniERP.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace BttMiniERP.Tests.Unit.Purchasing;

public sealed class PurchaseConfirmationRulesTests
{
    private static PurchaseReceipt Draft() => new() { Id = 7, CreatedByUserId = "creator", RowVersion = new byte[8],
        Lines = [new() { Id = 11, ItemId = 2, Quantity = 1.125m }, new() { Id = 12, ItemId = 1, Quantity = 2 }] };

    [Theory]
    [InlineData(DocumentStatus.Confirmed)] [InlineData(DocumentStatus.Cancelled)]
    public void OnlyDraftCanConfirm(DocumentStatus status)
    {
        var receipt = Draft(); receipt.Status = status;
        Assert.Equal(409, Assert.Throws<ApiException>(() => PurchaseReceiptRules.ConfirmationMovements(receipt)).StatusCode);
    }
    [Theory]
    [InlineData("empty")] [InlineData("duplicate")] [InlineData("zero")] [InlineData("precision")]
    public void RejectsInvalidStoredLines(string kind)
    {
        var receipt = Draft();
        if (kind == "empty") receipt.Lines.Clear();
        if (kind == "duplicate") receipt.Lines[1].ItemId = 2;
        if (kind == "zero") receipt.Lines[0].Quantity = 0;
        if (kind == "precision") receipt.Lines[0].Quantity = .0001m;
        Assert.Equal(409, Assert.Throws<ApiException>(() => PurchaseReceiptRules.ConfirmationMovements(receipt)).StatusCode);
    }
    [Fact]
    public void VersionAndServerAuditAndSourceAreExplicit()
    {
        var receipt = Draft();
        PurchaseReceiptRules.RequireVersion(receipt, new byte[8]);
        Assert.Equal(409, Assert.Throws<ApiException>(() => PurchaseReceiptRules.RequireVersion(receipt, [1,0,0,0,0,0,0,0])).StatusCode);
        var movements = PurchaseReceiptRules.ConfirmationMovements(receipt);
        Assert.Equal(new[] { 1, 2 }, movements.Select(x => x.ItemId));
        Assert.All(movements, x => { Assert.Equal(7, x.SourceDocumentId); Assert.Equal(SourceDocumentType.PurchaseReceipt, x.SourceDocumentType);
            Assert.Equal(InventoryTransactionType.PurchaseReceipt, x.TransactionType); });
        Assert.Equal(12, movements[0].SourceLineId); Assert.Equal(1.125m, movements[1].QuantityDelta);
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        PurchaseReceiptRules.MarkConfirmed(receipt, "confirmer", now);
        Assert.Equal(DocumentStatus.Confirmed, receipt.Status);
        Assert.Equal(now, receipt.ConfirmedAt); Assert.Equal(now, receipt.UpdatedAt);
        Assert.Equal("confirmer", receipt.ConfirmedByUserId); Assert.Equal("confirmer", receipt.UpdatedByUserId);
        Assert.Equal("creator", receipt.CreatedByUserId); Assert.Null(receipt.CancelledAt);
    }
    [Fact]
    public void ConfirmationAuditMigrationIsAdditiveAndNullable()
    {
        using var db = new ApplicationDbContextFactory().CreateDbContext(["--offline"]);
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations.Last().Value, db.Database.ProviderName!);
        var columns = migration.UpOperations.OfType<AddColumnOperation>().ToArray();
        Assert.Equal(new[] { "ConfirmedAt", "ConfirmedByUserId" }, columns.Select(x => x.Name).Order());
        Assert.All(columns, x => { Assert.Equal("PurchaseReceipts", x.Table); Assert.True(x.IsNullable); });
        var fk = Assert.Single(migration.UpOperations.OfType<AddForeignKeyOperation>());
        Assert.Equal(ReferentialAction.NoAction, fk.OnDelete); Assert.Equal("AspNetUsers", fk.PrincipalTable);
        Assert.All(migration.UpOperations, x => Assert.True(x is AddColumnOperation or CreateIndexOperation or AddForeignKeyOperation));
        Assert.False(db.GetService<IMigrator>().HasPendingModelChanges());
    }
    [Fact]
    public async Task MalformedVersionFailsBeforeDatabaseAccess()
    {
        using var db = new ApplicationDbContextFactory().CreateDbContext(["--offline"]);
        var service = new PurchaseReceiptService(db, null!, TimeProvider.System, null!);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => service.ConfirmAsync(1, "bad", default))).StatusCode);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }
}
