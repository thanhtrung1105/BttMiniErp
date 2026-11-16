using BttMiniERP.Api.Common;
using BttMiniERP.Api.Enums;

namespace BttMiniERP.Api.Services;

// Contract nội bộ, không bind từ HTTP. Caller lấy source từ chứng từ/dòng đã lưu.
public sealed record InventoryMovement(int ItemId, decimal QuantityDelta, InventoryTransactionType TransactionType,
    SourceDocumentType SourceDocumentType, int SourceDocumentId, int SourceLineId)
{
    public const decimal MaximumQuantity = 999999999999999.999m;

    public static InventoryMovement[] ValidateAndOrder(IReadOnlyList<InventoryMovement>? movements, string actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId) || actorId.Length > 128)
            throw new ApiException(400, "Người thực hiện kho không hợp lệ.");
        if (movements == null || movements.Count == 0)
            throw new ApiException(400, "Phải có ít nhất một biến động kho.");
        foreach (var movement in movements)
        {
            if (movement == null || movement.ItemId <= 0 || movement.SourceDocumentId <= 0
                || movement.QuantityDelta == 0 || movement.QuantityDelta > MaximumQuantity || movement.QuantityDelta < -MaximumQuantity
                || ((decimal.GetBits(movement.QuantityDelta)[3] >> 16) & 0xff) > 3)
                throw new ApiException(400, "Số lượng hoặc nguồn kho không hợp lệ.");
            var valid = movement.TransactionType switch
            {
                InventoryTransactionType.PurchaseReceipt => movement.SourceDocumentType == SourceDocumentType.PurchaseReceipt && movement.SourceLineId > 0 && movement.QuantityDelta > 0,
                InventoryTransactionType.ProductionConsumption => movement.SourceDocumentType == SourceDocumentType.ProductionOrder && movement.SourceLineId > 0 && movement.QuantityDelta < 0,
                InventoryTransactionType.ProductionOutput => movement.SourceDocumentType == SourceDocumentType.ProductionOrder && movement.SourceLineId == 0 && movement.QuantityDelta > 0,
                InventoryTransactionType.SalesIssue => movement.SourceDocumentType == SourceDocumentType.SalesOrder && movement.SourceLineId > 0 && movement.QuantityDelta < 0,
                _ => false
            };
            if (!valid) throw new ApiException(400, "Loại nguồn, loại biến động, dòng nguồn hoặc dấu số lượng không hợp lệ.");
        }
        if (movements.Select(x => (x.SourceDocumentType, x.SourceDocumentId, x.SourceLineId, x.TransactionType)).Distinct().Count() != movements.Count)
            throw new ApiException(409, "Nguồn biến động bị trùng trong yêu cầu.");
        return movements.OrderBy(x => x.ItemId).ThenBy(x => x.SourceDocumentType).ThenBy(x => x.SourceDocumentId)
            .ThenBy(x => x.SourceLineId).ThenBy(x => x.TransactionType).ToArray();
    }
}
