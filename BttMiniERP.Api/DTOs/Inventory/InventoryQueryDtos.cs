using System.ComponentModel.DataAnnotations;
using BttMiniERP.Api.Enums;

namespace BttMiniERP.Api.DTOs.Inventory;

public abstract class InventoryQuery
{
    [Range(1, 1000000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [Range(1, int.MaxValue)] public int? ItemId { get; init; }
    [EnumDataType(typeof(ItemType))] public ItemType? ItemType { get; init; }
}

public sealed class InventoryBalanceQuery : InventoryQuery
{
    [StringLength(200)] public string? Search { get; init; }
    [Required, RegularExpression("^(itemId|code)$")] public string SortBy { get; init; } = "itemId";
    [Required, RegularExpression("^(asc|desc)$")] public string SortDirection { get; init; } = "asc";
}

public sealed class InventoryTransactionQuery : InventoryQuery, IValidatableObject
{
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    [EnumDataType(typeof(SourceDocumentType))] public SourceDocumentType? SourceDocumentType { get; init; }
    [Range(1, int.MaxValue)] public int? SourceDocumentId { get; init; }
    [EnumDataType(typeof(InventoryTransactionType))] public InventoryTransactionType? TransactionType { get; init; }
    [Required, RegularExpression("^(occurredAt|id)$")] public string SortBy { get; init; } = "occurredAt";
    [Required, RegularExpression("^(asc|desc)$")] public string SortDirection { get; init; } = "desc";

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (From.HasValue && To.HasValue && From >= To)
            yield return new ValidationResult("Mốc bắt đầu phải trước mốc kết thúc.", [nameof(From), nameof(To)]);
    }
}

public sealed record InventoryPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
public sealed record BalanceRow(int ItemId, string Code, string Name, ItemType ItemType, string Unit, decimal QuantityOnHand);
public sealed record TransactionRow(int Id, int ItemId, string Code, string Name, ItemType ItemType, string Unit,
    InventoryTransactionType TransactionType, decimal QuantityDelta, decimal BalanceAfter, DateTimeOffset OccurredAt,
    SourceDocumentType SourceDocumentType, int SourceDocumentId, int SourceLineId, string PerformedByUserId)
{
    public string SourceDocumentUrl => SourceDocumentType switch
    {
        SourceDocumentType.PurchaseReceipt => $"/api/v1/purchase-receipts/{SourceDocumentId}",
        SourceDocumentType.ProductionOrder => $"/api/v1/production-orders/{SourceDocumentId}",
        SourceDocumentType.SalesOrder => $"/api/v1/sales-orders/{SourceDocumentId}",
        _ => throw new InvalidOperationException("Loại chứng từ kho không hợp lệ.")
    };
}
