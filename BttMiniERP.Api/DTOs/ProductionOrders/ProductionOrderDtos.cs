using System.ComponentModel.DataAnnotations;
using BttMiniERP.Api.DTOs.MasterData;
using BttMiniERP.Api.Enums;

namespace BttMiniERP.Api.DTOs.ProductionOrders;

public sealed class CreateProductionOrderRequest
{
    [Range(1, int.MaxValue)] public int BomId { get; init; }
    public decimal Quantity { get; init; }
    [StringLength(1000)] public string? Note { get; init; }
}
public sealed class UpdateProductionOrderRequest
{
    public decimal Quantity { get; init; }
    [StringLength(1000)] public string? Note { get; init; }
    [Required, RowVersion] public string RowVersion { get; init; } = "";
}
public sealed class ConfirmProductionOrderRequest
{
    [Required, RowVersion] public string RowVersion { get; init; } = "";
}
public sealed class CancelProductionOrderRequest
{
    [Required, RowVersion] public string RowVersion { get; init; } = "";
}
public sealed class ProductionOrderQuery : IValidatableObject
{
    [Range(1, 1000000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [Range(1, int.MaxValue)] public int? BomId { get; init; }
    [Range(1, int.MaxValue)] public int? FinishedProductId { get; init; }
    [EnumDataType(typeof(DocumentStatus))] public DocumentStatus? Status { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    [Required, RegularExpression("^(id|createdAt)$")] public string SortBy { get; init; } = "createdAt";
    [Required, RegularExpression("^(asc|desc)$")] public string SortDirection { get; init; } = "desc";
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (From.HasValue && To.HasValue && From >= To) yield return new("Khoảng ngày phải có from < to.");
    }
}
public sealed record ProductionOrderMaterialResponse(int Id, int RawMaterialId, decimal QuantityPerUnitSnapshot,
    decimal RequiredQuantity, decimal? ActualQuantity);
public sealed record ProductionOrderSummary(int Id, string OrderNumber, int BomId, int BomVersionSnapshot,
    int FinishedProductId, decimal Quantity, DocumentStatus Status, string? Note, DateTimeOffset CreatedAt,
    string CreatedByUserId, DateTimeOffset? UpdatedAt, string? UpdatedByUserId, DateTimeOffset? CancelledAt,
    string? CancelledByUserId, DateTimeOffset? ConfirmedAt, string? ConfirmedByUserId, string RowVersion);
public sealed record ProductionOrderResponse(int Id, string OrderNumber, int BomId, int BomVersionSnapshot,
    int FinishedProductId, decimal Quantity, DocumentStatus Status, string? Note, DateTimeOffset CreatedAt,
    string CreatedByUserId, DateTimeOffset? UpdatedAt, string? UpdatedByUserId, DateTimeOffset? CancelledAt,
    string? CancelledByUserId, DateTimeOffset? ConfirmedAt, string? ConfirmedByUserId, string RowVersion,
    IReadOnlyList<ProductionOrderMaterialResponse> Materials);
public sealed record ProductionOrderPage(IReadOnlyList<ProductionOrderSummary> Items, int Page, int PageSize, int TotalCount);
