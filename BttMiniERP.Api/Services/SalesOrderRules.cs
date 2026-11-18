using System.ComponentModel.DataAnnotations;
using BttMiniERP.Api.Common;
using BttMiniERP.Api.DTOs.SalesOrders;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
namespace BttMiniERP.Api.Services;

public static class SalesOrderRules
{
    public static void RequireVersion(SalesOrder order, byte[] version)
    {
        if (!order.RowVersion.SequenceEqual(version)) throw new ApiException(409, "Đơn đã thay đổi; tải lại RowVersion.");
    }
    public const decimal MaximumMoney = 9999999999999999.99m;
    public static void Validate(object value)
    {
        if (!Validator.TryValidateObject(value, new ValidationContext(value), [], true))
            throw new ApiException(400, "Dữ liệu đơn bán không hợp lệ.");
    }
    public static SalesOrderLine[] Calculate(CreateSalesOrderRequest request)
    {
        Validate(request);
        var lines = new List<SalesOrderLine>();
        var seen = new HashSet<int>();
        decimal total = 0;
        foreach (var input in request.Lines)
        {
            if (input == null) throw new ApiException(400, "Dòng đơn không được null.");
            Validate(input);
            if (!seen.Add(input.ItemId)) throw new ApiException(400, "Một thành phẩm chỉ được xuất hiện một lần.");
            if (input.Quantity <= 0 || input.Quantity > InventoryMovement.MaximumQuantity
                || ((decimal.GetBits(input.Quantity)[3] >> 16) & 255) > 3)
                throw new ApiException(400, "Số lượng phải dương và thuộc decimal(18,3).");
            decimal amount;
            try
            {
                amount = decimal.Round(input.Quantity * input.UnitPrice, 2, MidpointRounding.AwayFromZero);
                total += amount;
            }
            catch (OverflowException) { throw new ApiException(400, "Tổng tiền vượt giới hạn."); }
            if (amount > MaximumMoney || total > MaximumMoney) throw new ApiException(400, "Tổng tiền vượt decimal(18,2).");
            lines.Add(new() { LineNumber = lines.Count + 1, ItemId = input.ItemId, Quantity = input.Quantity, UnitPrice = input.UnitPrice, LineTotal = amount });
        }
        return lines.ToArray();
    }
    public static void Cancel(SalesOrder order, string actorId, DateTimeOffset now)
    {
        RequireDraft(order.Status);
        order.Status = DocumentStatus.Cancelled;
        order.CancelledAt = order.UpdatedAt = now;
        order.CancelledByUserId = order.UpdatedByUserId = actorId;
    }

    public static InventoryMovement[] ConfirmationMovements(SalesOrder order)
    {
        RequireDraft(order.Status);
        if (order.Lines.Count == 0) throw new ApiException(409, "Đơn bán chưa có dòng thành phẩm.");
        return order.Lines.Select(line => new InventoryMovement(line.ItemId, -line.Quantity,
            InventoryTransactionType.SalesIssue, SourceDocumentType.SalesOrder, order.Id, line.Id)).ToArray();
    }

    public static void RequireDraft(DocumentStatus status)
    {
        if (status != DocumentStatus.Draft) throw new ApiException(409, "Đơn bán đã kết thúc.");
    }
    public static void RequireCustomer(Customer? customer)
    {
        if (customer == null) throw new ApiException(404, "Không tìm thấy khách hàng.");
        if (!customer.IsActive) throw new ApiException(409, "Khách hàng đã vô hiệu.");
    }
    public static void RequireProduct(Item? item)
    {
        if (item == null) throw new ApiException(404, "Không tìm thấy Item.");
        if (!item.IsActive || item.ItemType != ItemType.FinishedProduct) throw new ApiException(409, "Chỉ bán thành phẩm đang hoạt động.");
    }
}
