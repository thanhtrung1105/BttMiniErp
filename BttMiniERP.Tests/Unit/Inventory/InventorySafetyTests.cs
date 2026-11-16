using BttMiniERP.Api.Data;
using BttMiniERP.Api.Entities;
using BttMiniERP.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Tests.Unit.Inventory;

public sealed class InventorySafetyTests
{
    [Theory]
    [InlineData("Inventory", "BttMiniERP_InventoryTest", "INVENTORY", "Verify,TemporaryWrites,Concurrency,CleanupOwnedRecords")]
    [InlineData("Purchasing", "BttMiniERP_PurchasingTest", "PURCHASING", "Verify,TemporaryWrites,Concurrency,CleanupOwnedRecords")]
    [InlineData("Confirmation", "BttMiniERP_PurchaseConfirmationTest", "CONFIRMATION", "Verify,TemporaryWrites,Concurrency,CleanupOwnedRecords")]
    [InlineData("InventoryMigration", "BttMiniERP_InventoryMigrationTest", "INVENTORY_MIGRATION", "Verify,MigrationUpRollback,TemporaryWrites,CleanupOwnedRecords")]
    public void RequiresExactOptInTargetAndNoMars(string profile, string database, string flag, string operations)
    {
        var settings = new Dictionary<string, string> {
            ["BTT_RUN_SQL_TESTS"] = "1", [$"BTT_RUN_{flag}_SQL_TESTS"] = "1",
            ["BTT_SQL_TEST_PROFILE"] = profile, ["BTT_SQL_TEST_ENVIRONMENT"] = "Testing",
            ["BTT_SQL_APPROVED_SERVER"] = "localhost", [$"BTT_{flag}_SQL_APPROVED_OPERATIONS"] = operations,
            ["BTT_SQL_TEST_CONNECTION_STRING"] = $"Server=localhost;Database={database};Integrated Security=True;Encrypt=True;TrustServerCertificate=True" };
        string? Read(string key) => settings.GetValueOrDefault(key);
        Assert.Contains(database, MasterDataSqlFixture.ReadSettings(Read, profile));
        foreach (var key in settings.Keys.ToArray())
        {
            var original = settings[key]; settings.Remove(key);
            Assert.Throws<InvalidOperationException>(() => MasterDataSqlFixture.ReadSettings(Read, profile));
            settings[key] = original;
        }
        var connection = settings["BTT_SQL_TEST_CONNECTION_STRING"];
        foreach (var invalid in new[] { connection.Replace(database, "BttMiniERP_Test"), connection.Replace(database, "BttMiniERP_Dev"),
            connection.Replace("localhost", "remote"), connection + ";MultipleActiveResultSets=True", connection.Replace("Encrypt=True", "Encrypt=False") })
        {
            settings["BTT_SQL_TEST_CONNECTION_STRING"] = invalid;
            Assert.Throws<InvalidOperationException>(() => MasterDataSqlFixture.ReadSettings(Read, profile));
        }
    }

    [Theory]
    [InlineData("update")][InlineData("delete")][InlineData("nonzero")]
    public async Task OrdinaryEfCannotChangeBalance(string operation)
    {
        using var db = new ApplicationDbContextFactory().CreateDbContext(["--offline"]);
        var balance = new InventoryBalance { ItemId = 1, CreatedByUserId = "actor" };
        if (operation == "nonzero") { balance.QuantityOnHand = 1; db.Add(balance); }
        else { db.Attach(balance); if (operation == "delete") db.Remove(balance); else balance.QuantityOnHand = 1; }
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }
}
