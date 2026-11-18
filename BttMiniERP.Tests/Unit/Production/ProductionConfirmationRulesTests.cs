using BttMiniERP.Api.Common;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
using BttMiniERP.Api.Services;

namespace BttMiniERP.Tests.Unit.Production;

public sealed class ProductionConfirmationRulesTests
{
    private static ProductionOrder Draft() => new()
    {
        Id = 7, FinishedProductId = 1, BomVersionSnapshot = 2, Quantity = 3,
        Materials = [new() { Id = 12, ProductionOrderId = 7, RawMaterialId = 5,
            QuantityPerUnitSnapshot = 2, RequiredQuantity = 6 }]
    };

    [Fact]
    public void PostingUsesPersistedSourceIdsAndSignedSnapshotQuantities()
    {
        Assert.Equal(new InventoryMovement[] {
            new(5, -6, InventoryTransactionType.ProductionConsumption, SourceDocumentType.ProductionOrder, 7, 12),
            new(1, 3, InventoryTransactionType.ProductionOutput, SourceDocumentType.ProductionOrder, 7, 0)
        }, ProductionOrderRules.ConfirmationMovements(Draft()));
    }

    [Theory]
    [InlineData("empty")] [InlineData("version")] [InlineData("wrong order")] [InlineData("actual")]
    [InlineData("required")] [InlineData("per unit")] [InlineData("self")] [InlineData("duplicate")]
    [InlineData("cancelled")] [InlineData("confirmed")]
    public void InvalidSnapshotsAndTerminalOrdersAreRejected(string scenario)
    {
        var order = Draft();
        var line = order.Materials[0];
        switch (scenario)
        {
            case "empty": order.Materials.Clear(); break;
            case "version": order.BomVersionSnapshot = 0; break;
            case "wrong order": line.ProductionOrderId++; break;
            case "actual": line.ActualQuantity = 6; break;
            case "required": line.RequiredQuantity = 5; break;
            case "per unit": line.QuantityPerUnitSnapshot = 0; break;
            case "self": line.RawMaterialId = order.FinishedProductId; break;
            case "duplicate": order.Materials.Add(line); break;
            case "cancelled": order.Status = DocumentStatus.Cancelled; break;
            case "confirmed": order.Status = DocumentStatus.Confirmed; break;
        }
        Assert.Equal(409, Assert.Throws<ApiException>(() => ProductionOrderRules.ConfirmationMovements(order)).StatusCode);
    }
}
