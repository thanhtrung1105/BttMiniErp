using System.ComponentModel.DataAnnotations;
using BttMiniERP.Api.DTOs.MasterData;
using BttMiniERP.Api.Enums;
namespace BttMiniERP.Api.DTOs.PurchaseReceipts;

public class CreatePurchaseReceiptRequest
{
    [Range(1, int.MaxValue)] public int SupplierId { get; init; }
    public DateTimeOffset? ReceiptDate { get; init; }
    [StringLength(1000)] public string? Note { get; init; }
    [Required] public List<PurchaseReceiptLineRequest> Lines { get; init; } = [];
}
public sealed class UpdatePurchaseReceiptRequest : CreatePurchaseReceiptRequest
{
    [Required, RowVersion] public string RowVersion { get; init; } = "";
}
public sealed class CancelPurchaseReceiptRequest
{
    [Required, RowVersion] public string RowVersion { get; init; } = "";
}
public sealed class ConfirmPurchaseReceiptRequest
{
    [Required, RowVersion] public string RowVersion { get; init; } = "";
}
public sealed class PurchaseReceiptLineRequest
{
    [Range(1, int.MaxValue)] public int ItemId { get; init; }
    public decimal Quantity { get; init; }
    [Price] public decimal UnitPrice { get; init; }
}
public sealed class PurchaseReceiptQuery : IValidatableObject
{
    [Range(1, 1000000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [Range(1, int.MaxValue)] public int? SupplierId { get; init; }
    [EnumDataType(typeof(DocumentStatus))] public DocumentStatus? Status { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    [Required, RegularExpression("^(id|receiptDate)$")] public string SortBy { get; init; } = "receiptDate";
    [Required, RegularExpression("^(asc|desc)$")] public string SortDirection { get; init; } = "desc";
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (From.HasValue && To.HasValue && From >= To) yield return new("Khoảng ngày phải có from < to.");
    }
}
public sealed record PurchaseReceiptLineResponse(int Id, int LineNumber, int ItemId, decimal Quantity, decimal UnitPrice, decimal LineTotal);
public sealed record PurchaseReceiptSummary(int Id, string ReceiptNumber, int SupplierId, DateTimeOffset ReceiptDate,
    DocumentStatus Status, decimal TotalAmount, string? Note, DateTimeOffset CreatedAt, string CreatedByUserId,
    DateTimeOffset? UpdatedAt, string? UpdatedByUserId, DateTimeOffset? CancelledAt, string? CancelledByUserId, string RowVersion,
    DateTimeOffset? ConfirmedAt = null, string? ConfirmedByUserId = null);
public sealed record PurchaseReceiptResponse(int Id, string ReceiptNumber, int SupplierId, DateTimeOffset ReceiptDate,
    DocumentStatus Status, decimal TotalAmount, string? Note, DateTimeOffset CreatedAt, string CreatedByUserId,
    DateTimeOffset? UpdatedAt, string? UpdatedByUserId, DateTimeOffset? CancelledAt, string? CancelledByUserId,
    string RowVersion, IReadOnlyList<PurchaseReceiptLineResponse> Lines,
    DateTimeOffset? ConfirmedAt = null, string? ConfirmedByUserId = null);
public sealed record PurchaseReceiptPage(IReadOnlyList<PurchaseReceiptSummary> Items, int Page, int PageSize, int TotalCount);
