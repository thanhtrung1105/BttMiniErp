using System.ComponentModel.DataAnnotations;
using BttMiniERP.Api.Common;
using BttMiniERP.Api.DTOs.PurchaseReceipts;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
namespace BttMiniERP.Api.Services;

public static class PurchaseReceiptRules
{
    public static void RequireVersion(PurchaseReceipt receipt, byte[] version)
    {
        if (!receipt.RowVersion.SequenceEqual(version)) throw new ApiException(409, "Phiếu đã thay đổi; tải lại RowVersion.");
    }
    public static InventoryMovement[] ConfirmationMovements(PurchaseReceipt receipt)
    {
        RequireDraft(receipt.Status);
        if (receipt.Lines.Count == 0) throw new ApiException(409, "Phiếu nhập phải có ít nhất một dòng để xác nhận.");
        if (receipt.Lines.Select(x => x.ItemId).Distinct().Count() != receipt.Lines.Count)
            throw new ApiException(409, "Phiếu có Item trùng.");
        var movements = receipt.Lines.Select(x => new InventoryMovement(x.ItemId, x.Quantity,
            InventoryTransactionType.PurchaseReceipt, SourceDocumentType.PurchaseReceipt, receipt.Id, x.Id)).ToArray();
        try { return InventoryMovement.ValidateAndOrder(movements, receipt.CreatedByUserId); }
        catch (ApiException) { throw new ApiException(409, "Dòng phiếu nhập không hợp lệ để xác nhận."); }
    }
    public static void MarkConfirmed(PurchaseReceipt receipt, string actorId, DateTimeOffset now)
    {
        receipt.Status = DocumentStatus.Confirmed;
        receipt.ConfirmedAt = receipt.UpdatedAt = now;
        receipt.ConfirmedByUserId = receipt.UpdatedByUserId = actorId;
    }
    public const decimal MaximumMoney = 9999999999999999.99m;
    public static void Validate(object value)
    {
        if (!Validator.TryValidateObject(value, new ValidationContext(value), [], true))
            throw new ApiException(400, "Dữ liệu phiếu nhập không hợp lệ.");
    }
    public static PurchaseReceiptLine[] Calculate(CreatePurchaseReceiptRequest request)
    {
        Validate(request);
        var lines = new List<PurchaseReceiptLine>(); var seen = new HashSet<int>(); decimal total = 0;
        foreach (var input in request.Lines)
        {
            if (input == null) throw new ApiException(400, "Dòng phiếu không được null.");
            Validate(input);
            if (!seen.Add(input.ItemId) || input.Quantity <= 0 || input.Quantity > InventoryMovement.MaximumQuantity
                || ((decimal.GetBits(input.Quantity)[3] >> 16) & 255) > 3)
                throw new ApiException(400, "Item trùng hoặc số lượng không hợp lệ.");
            decimal amount;
            try { amount = decimal.Round(checked(input.Quantity * input.UnitPrice), 2, MidpointRounding.AwayFromZero); total = checked(total + amount); }
            catch (OverflowException) { throw new ApiException(400, "Tổng tiền vượt giới hạn."); }
            if (amount > MaximumMoney || total > MaximumMoney) throw new ApiException(400, "Tổng tiền vượt decimal(18,2).");
            lines.Add(new() { LineNumber = lines.Count + 1, ItemId = input.ItemId, Quantity = input.Quantity, UnitPrice = input.UnitPrice, LineTotal = amount });
        }
        return lines.ToArray();
    }
    public static void RequireDraft(DocumentStatus status)
    {
        if (status != DocumentStatus.Draft) throw new ApiException(409, "Chỉ được sửa hoặc hủy phiếu Draft.");
    }
    public static void RequireSupplier(Supplier? supplier)
    {
        if (supplier == null) throw new ApiException(404, "Không tìm thấy nhà cung cấp.");
        if (!supplier.IsActive) throw new ApiException(409, "Nhà cung cấp đã vô hiệu.");
    }
    public static void RequireMaterial(Item? item)
    {
        if (item == null) throw new ApiException(404, "Không tìm thấy Item.");
        if (!item.IsActive || item.ItemType != ItemType.RawMaterial) throw new ApiException(409, "Chỉ nhận nguyên vật liệu đang hoạt động.");
    }
}
