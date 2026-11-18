using System.Globalization;
using BttMiniERP.Api.Common;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.Data.Migrations;
using BttMiniERP.Api.DTOs.SalesOrders;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
using BttMiniERP.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
namespace BttMiniERP.Tests.Unit.Sales;

public sealed class SalesOrderRulesTests
{
    [Fact]
    public void ConfirmationUsesPersistedLineIdentityAndNegativeQuantity()
    {
        var order = new SalesOrder { Id = 7, Lines = [new() { Id = 21, ItemId = 12, Quantity = 2.345m }] };
        Assert.Equal(new InventoryMovement(12, -2.345m, InventoryTransactionType.SalesIssue,
            SourceDocumentType.SalesOrder, 7, 21), Assert.Single(SalesOrderRules.ConfirmationMovements(order)));
    }

    [Theory]
    [InlineData(DocumentStatus.Cancelled, true)]
    [InlineData(DocumentStatus.Confirmed, true)]
    [InlineData(DocumentStatus.Draft, false)]
    public void TerminalOrEmptyOrdersCannotPost(DocumentStatus status, bool hasLine)
    {
        var order = new SalesOrder { Status = status };
        if (hasLine) order.Lines.Add(new() { Id = 2, ItemId = 3, Quantity = 1 });
        Assert.Equal(409, Assert.Throws<ApiException>(() => SalesOrderRules.ConfirmationMovements(order)).StatusCode);
    }

    [Fact]
    public void SalesSqlProfileRequiresItsOwnOptInAndDatabase()
    {
        var settings = new Dictionary<string,string> {
            ["BTT_RUN_SQL_TESTS"] = "1", ["BTT_RUN_SALES_SQL_TESTS"] = "1",
            ["BTT_SQL_TEST_ENVIRONMENT"] = "Testing", ["BTT_SQL_TEST_PROFILE"] = "Sales",
            ["BTT_SQL_APPROVED_SERVER"] = "localhost",
            ["BTT_SALES_SQL_APPROVED_OPERATIONS"] = "Verify,TemporaryWrites,Concurrency,CleanupOwnedRecords",
            ["BTT_SQL_TEST_CONNECTION_STRING"] = "Server=localhost;Database=BttMiniERP_SalesTest;Integrated Security=True;Encrypt=True"
        };
        string Read() => BttMiniERP.Tests.Fixtures.MasterDataSqlFixture.ReadSettings(key => settings.GetValueOrDefault(key), "Sales");
        Assert.Contains("BttMiniERP_SalesTest", Read());
        settings["BTT_RUN_SALES_CONFIRM_SQL_TESTS"] = "1";
        Assert.Throws<InvalidOperationException>(() => Read());
        var originalConnection = settings["BTT_SQL_TEST_CONNECTION_STRING"];
        settings["BTT_SQL_TEST_CONNECTION_STRING"] = originalConnection.Replace("SalesTest", "SalesConfirmTest");
        Assert.Contains("BttMiniERP_SalesConfirmTest", Read());
        settings.Remove("BTT_RUN_SALES_CONFIRM_SQL_TESTS");
        Assert.Throws<InvalidOperationException>(() => Read());
        settings["BTT_SQL_TEST_CONNECTION_STRING"] = originalConnection;
        settings["BTT_RUN_SALES_SQL_TESTS"] = "0";
        Assert.Throws<InvalidOperationException>(() => Read());
        settings["BTT_RUN_SALES_SQL_TESTS"] = "1";
        settings["BTT_SQL_TEST_CONNECTION_STRING"] = settings["BTT_SQL_TEST_CONNECTION_STRING"].Replace("SalesTest", "Dev");
        Assert.Throws<InvalidOperationException>(() => Read());
    }

    [Theory]
    [InlineData("0", "1")]
    [InlineData("-1", "1")]
    [InlineData("1.0001", "1")]
    [InlineData("1", "-1")]
    [InlineData("1", "1.001")]
    [InlineData("1000000000000000", "1")]
    [InlineData("999999999999999.999", "9999999999999999.99")]
    [InlineData("2", "9999999999999999.99")]
    public void RejectsInvalidPrecisionOrOverflow(string quantity, string price)
    {
        Assert.Equal(400, Assert.Throws<ApiException>(() => SalesOrderRules.Calculate(new()
        {
            CustomerId = 1,
            Lines = [new() {
            ItemId = 1, Quantity = decimal.Parse(quantity, CultureInfo.InvariantCulture), UnitPrice = decimal.Parse(price, CultureInfo.InvariantCulture) }]
        })).StatusCode);
    }
    [Fact]
    public void EmptyDraftRoundingDuplicateNullAndTotalOverflow()
    {
        Assert.Equal(400, Assert.Throws<ApiException>(() => SalesOrderRules.Calculate(new() { CustomerId = 1 })).StatusCode);
        var lines = SalesOrderRules.Calculate(new() { CustomerId = 1, Lines = [new() { ItemId = 1, Quantity = .125m, UnitPrice = .20m }, new() { ItemId = 2, Quantity = 1, UnitPrice = 0 }] });
        Assert.Equal(.03m, lines.Sum(x => x.LineTotal)); Assert.Equal(new[] { 1, 2 }, lines.Select(x => x.LineNumber));
        foreach (var invalid in new[] {
            new CreateSalesOrderRequest { CustomerId = 0 }, new CreateSalesOrderRequest { CustomerId = 1, Lines = null! },
            new CreateSalesOrderRequest { CustomerId = 1, Lines = [null!] },
            new CreateSalesOrderRequest { CustomerId = 1, Lines = [new() { ItemId = 1, Quantity = 1 },new() { ItemId = 1, Quantity = 1 }] },
            new CreateSalesOrderRequest { CustomerId = 1, Lines = [new() { ItemId = 1, Quantity = 1, UnitPrice = SalesOrderRules.MaximumMoney },new() { ItemId = 2, Quantity = 1, UnitPrice = 1 }] } })
            Assert.Throws<ApiException>(() => SalesOrderRules.Calculate(invalid));
    }
    [Theory]
    [InlineData(ItemType.FinishedProduct, true, true)]
    [InlineData(ItemType.FinishedProduct, false, false)]
    [InlineData(ItemType.RawMaterial, true, false)]
    [InlineData((ItemType)99, true, false)]
    public void OnlyActiveFinishedProducts(ItemType type, bool active, bool valid)
    {
        var error = Record.Exception(() => SalesOrderRules.RequireProduct(new Item { ItemType = type, IsActive = active }));
        if (valid) Assert.Null(error); else Assert.Equal(409, Assert.IsType<ApiException>(error).StatusCode);
    }
    [Fact]
    public void CustomerAndDraftGuards()
    {
        SalesOrderRules.RequireCustomer(new Customer());
        Assert.Equal(404, Assert.Throws<ApiException>(() => SalesOrderRules.RequireCustomer(null)).StatusCode);
        Assert.Equal(404, Assert.Throws<ApiException>(() => SalesOrderRules.RequireProduct(null)).StatusCode);
        Assert.Equal(409, Assert.Throws<ApiException>(() => SalesOrderRules.RequireCustomer(new() { IsActive = false })).StatusCode);
        SalesOrderRules.RequireDraft(DocumentStatus.Draft);
        foreach (var state in new[] { DocumentStatus.Cancelled, DocumentStatus.Confirmed, (DocumentStatus)99 })
            Assert.Equal(409, Assert.Throws<ApiException>(() => SalesOrderRules.RequireDraft(state)).StatusCode);
    }
    [Fact]
    public void CancellationKeepsLinesAndRecordsServerActorAndTime()
    {
        var order = new SalesOrder { Lines = [new() { Quantity = 2, UnitPrice = 3, LineTotal = 6 }], TotalAmount = 6 };
        SalesOrderRules.Cancel(order, "sales-actor", DateTimeOffset.UnixEpoch);
        Assert.Equal(DocumentStatus.Cancelled, order.Status);
        Assert.Equal("sales-actor", order.CancelledByUserId);
        Assert.Equal(order.CancelledByUserId, order.UpdatedByUserId);
        Assert.Equal(DateTimeOffset.UnixEpoch, order.CancelledAt);
        Assert.Equal(order.CancelledAt, order.UpdatedAt);
        Assert.Equal(order.TotalAmount, Assert.Single(order.Lines).LineTotal);
        Assert.Equal(409, Assert.Throws<ApiException>(() => SalesOrderRules.Cancel(order, "other", DateTimeOffset.UtcNow)).StatusCode);
        Assert.Equal("sales-actor", order.CancelledByUserId);
    }

    [Fact]
    public void GeneratorUsesApprovedFormatAndIndependentCalls()
    {
        var numbers = Enumerable.Range(0, 2000).AsParallel().Select(_ => DocumentNumberGenerator.NewSalesOrder()).ToArray();
        Assert.All(numbers, n => Assert.Matches("^SO-[A-F0-9]{32}$", n)); Assert.Equal(numbers.Length, numbers.Distinct().Count());
    }
    [Fact]
    public async Task InvalidServiceInputDoesNotConnectOrTrackWrites()
    {
        using var db = new ApplicationDbContextFactory().CreateDbContext(["--offline"]);
        var service = new SalesOrderService(db, null!, TimeProvider.System, null!);
        await Assert.ThrowsAsync<ApiException>(() => service.CreateAsync(new() { CustomerId = 0 }, default));
        await Assert.ThrowsAsync<ApiException>(() => service.ListAsync(new() { PageSize = 101 }, default));
        await Assert.ThrowsAsync<ApiException>(() => service.UpdateAsync(1, new() { CustomerId = 1, RowVersion = "bad" }, default));
        await Assert.ThrowsAsync<ApiException>(() => service.CancelAsync(1, "bad", default));
        Assert.Empty(db.ChangeTracker.Entries()); Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }
    [Fact]
    public void MappingMigrationAndSnapshotMatchAndDoNotTouchInventory()
    {
        using var db = new ApplicationDbContextFactory().CreateDbContext(["--offline"]);
        Assert.False(db.Database.HasPendingModelChanges());
        var header = db.Model.FindEntityType(typeof(SalesOrder))!;
        Assert.True(header.FindProperty("RowVersion")!.IsConcurrencyToken); Assert.Equal(40, header.FindProperty("OrderNumber")!.GetMaxLength());
        Assert.Equal("decimal(18,2)", header.FindProperty("TotalAmount")!.GetColumnType());
        var line = db.Model.FindEntityType(typeof(SalesOrderLine))!;
        Assert.Equal("decimal(18,3)", line.FindProperty("Quantity")!.GetColumnType());
        Assert.Equal("decimal(18,2)", line.FindProperty("UnitPrice")!.GetColumnType());
        var migration = new AddSalesOrders();
        Assert.All(migration.UpOperations, x => Assert.True(x is CreateTableOperation or CreateIndexOperation));
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(new[] { "SalesOrders", "SalesOrderLines" }, tables.Select(x => x.Name));
        Assert.Equal(7, tables.Sum(x => x.ForeignKeys.Count)); Assert.Equal(6, tables.Sum(x => x.CheckConstraints.Count));
        Assert.All(tables.SelectMany(x => x.ForeignKeys), x => Assert.Equal(ReferentialAction.NoAction, x.OnDelete));
        Assert.Equal(3, migration.UpOperations.OfType<CreateIndexOperation>().Count(x => x.IsUnique));
    }
}
