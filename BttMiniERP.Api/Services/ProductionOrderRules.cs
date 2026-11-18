using BttMiniERP.Api.Common;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;

namespace BttMiniERP.Api.Services;

public static class ProductionOrderRules
{
    public static void RequireQuantity(decimal quantity)
    {
        if (quantity <= 0 || quantity > InventoryMovement.MaximumQuantity
            || ((decimal.GetBits(quantity)[3] >> 16) & 255) > 3)
            throw new ApiException(400, "Sản lượng phải dương, thuộc decimal(18,3) và có tối đa ba chữ số thập phân.");
    }
    public static decimal RequiredQuantity(decimal perUnit, decimal quantity)
    {
        RequireQuantity(quantity);
        if (perUnit <= 0 || perUnit > InventoryMovement.MaximumQuantity
            || ((decimal.GetBits(perUnit)[3] >> 16) & 255) > 3)
            throw new ApiException(409, "Định mức snapshot không hợp lệ.");
        try
        {
            var product = checked(perUnit * quantity);
            if (product > InventoryMovement.MaximumQuantity) throw new OverflowException();
            return decimal.Ceiling(product * 1000m) / 1000m;
        }
        catch (OverflowException) { throw new ApiException(400, "Nhu cầu nguyên vật liệu vượt decimal(18,3)."); }
    }
    public static ProductionOrderMaterial[] Snapshot(BillOfMaterial? bom, decimal quantity)
    {
        RequireQuantity(quantity);
        if (bom == null) throw new ApiException(404, "Không tìm thấy BOM.");
        if (!bom.IsActive || bom.Version < 1 || bom.Details.Count == 0)
            throw new ApiException(409, "BOM không hoạt động hoặc không có định mức hợp lệ.");
        var seen = new HashSet<int>();
        return bom.Details.OrderBy(x => x.RawMaterialId).Select(x => {
            if (x.RawMaterialId <= 0 || x.RawMaterialId == bom.FinishedProductId || !seen.Add(x.RawMaterialId))
                throw new ApiException(409, "BOM có nguyên liệu trùng hoặc tự tham chiếu.");
            return new ProductionOrderMaterial { RawMaterialId = x.RawMaterialId, QuantityPerUnitSnapshot = x.QuantityPerUnit,
                RequiredQuantity = RequiredQuantity(x.QuantityPerUnit, quantity) };
        }).ToArray();
    }
    public static List<InventoryMovement> ConfirmationMovements(ProductionOrder order)
    {
        RequireDraft(order.Status);
        if (order.Materials.Count == 0 || order.BomVersionSnapshot < 1)
            throw new ApiException(409, "Lệnh thiếu snapshot định mức hợp lệ.");

        var movements = new List<InventoryMovement>();
        var materials = new HashSet<int>();
        foreach (var line in order.Materials)
        {
            if (line.Id <= 0 || line.ProductionOrderId != order.Id || line.ActualQuantity.HasValue
                || line.RawMaterialId <= 0 || line.RawMaterialId == order.FinishedProductId
                || !materials.Add(line.RawMaterialId)
                || line.RequiredQuantity != RequiredQuantity(line.QuantityPerUnitSnapshot, order.Quantity))
                throw new ApiException(409, "Chi tiết snapshot không hợp lệ.");

            movements.Add(new(line.RawMaterialId, -line.RequiredQuantity, InventoryTransactionType.ProductionConsumption,
                SourceDocumentType.ProductionOrder, order.Id, line.Id));
        }
        movements.Add(new(order.FinishedProductId, order.Quantity, InventoryTransactionType.ProductionOutput,
            SourceDocumentType.ProductionOrder, order.Id, 0));
        return movements;
    }

    public static void RequireDraft(DocumentStatus status)
    {
        if (status != DocumentStatus.Draft) throw new ApiException(409, "Thao tác yêu cầu lệnh sản xuất ở trạng thái Draft.");
    }
}
