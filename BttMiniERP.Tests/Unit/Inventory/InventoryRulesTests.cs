using BttMiniERP.Api.Common;
using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BttMiniERP.Tests.Unit.Inventory;

public sealed class InventoryRulesTests
{
    [Theory]
    [InlineData(0, 0, 1, "1.001", true)][InlineData(1, 1, 1, "-1.001", true)]
    [InlineData(2, 1, 0, "1", true)][InlineData(3, 2, 1, "-1", true)]
    [InlineData(0, 0, 1, "-1", false)][InlineData(1, 1, 1, "1", false)]
    [InlineData(2, 1, 0, "-1", false)][InlineData(3, 2, 1, "1", false)]
    [InlineData(0, 0, 1, "0", false)][InlineData(0, 0, 1, "1.0001", false)]
    [InlineData(0, 0, 1, "1000000000000000", false)][InlineData(0, 0, 1, "999999999999999.999", true)]
    [InlineData(99, 0, 1, "1", false)][InlineData(0, 99, 1, "1", false)]
    [InlineData(0, 1, 1, "1", false)][InlineData(2, 1, 1, "1", false)]
    [InlineData(3, 2, 0, "-1", false)]
    public void EnforcesTypeSourceSignAndQuantity(int type, int source, int line, string quantity, bool valid)
    {
        var movement = new InventoryMovement(1, decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture),
            (InventoryTransactionType)type, (SourceDocumentType)source, 1, line);
        var error = Record.Exception(() => InventoryMovement.ValidateAndOrder([movement], "actor"));
        if (valid) Assert.Null(error); else Assert.Equal(400, Assert.IsType<ApiException>(error).StatusCode);
    }

    [Fact]
    public void RequiresSourceActorAndOrdersWholeBatchWithoutMutatingInput()
    {
        var movement = new InventoryMovement(2, 1, InventoryTransactionType.PurchaseReceipt, SourceDocumentType.PurchaseReceipt, 1, 1);
        foreach (var bad in new[] { movement with { ItemId = 0 }, movement with { SourceDocumentId = 0 }, movement with { SourceLineId = 0 } })
            Assert.Equal(400, Assert.Throws<ApiException>(() => InventoryMovement.ValidateAndOrder([bad], "actor")).StatusCode);
        foreach (var actor in new[] { "", " ", new string('x', 129) })
            Assert.Equal(400, Assert.Throws<ApiException>(() => InventoryMovement.ValidateAndOrder([movement], actor)).StatusCode);
        Assert.Throws<ApiException>(() => InventoryMovement.ValidateAndOrder(null, "actor"));
        Assert.Throws<ApiException>(() => InventoryMovement.ValidateAndOrder([], "actor"));
        Assert.Equal(409, Assert.Throws<ApiException>(() => InventoryMovement.ValidateAndOrder([movement, movement with { ItemId = 1 }], "actor")).StatusCode);
        var input = new[] { movement, movement with { ItemId = 1, SourceLineId = 2 } };
        Assert.Equal(new[] { 1, 2 }, InventoryMovement.ValidateAndOrder(input, "actor").Select(x => x.ItemId));
        Assert.Equal(2, input[0].ItemId);
    }

    [Fact]
    public async Task InvalidInputOrMissingTransactionNeverOpensConnection()
    {
        using var db = new ApplicationDbContextFactory().CreateDbContext(["--offline"]);
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var service = new InventoryService(db, new CurrentUser(accessor), TimeProvider.System);
        Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => service.ApplyAsync([], default))).StatusCode);
        accessor.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "actor")], "test"));
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => service.ApplyAsync([], default))).StatusCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(
            [new(1, 1, InventoryTransactionType.PurchaseReceipt, SourceDocumentType.PurchaseReceipt, 1, 1)], default));
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task OrdinaryEfWritesCannotModifyOrDeleteLedger(bool delete)
    {
        using var db = new ApplicationDbContextFactory().CreateDbContext(["--offline"]);
        var row = new InventoryTransaction { Id = 1 };
        db.Attach(row);
        if (delete) db.Remove(row); else row.QuantityDelta = 1;
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }
}
