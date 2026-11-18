using System.ComponentModel.DataAnnotations;
using BttMiniERP.Api.DTOs.MasterData;
using BttMiniERP.Api.Enums;
namespace BttMiniERP.Api.DTOs.SalesOrders;

public class CreateSalesOrderRequest
{
    [Range(1, int.MaxValue)] public int CustomerId { get; init; }
    public DateTimeOffset? OrderDate { get; init; }
    [StringLength(1000)] public string? Note { get; init; }
    [Required, MinLength(1)] public List<SalesOrderLineRequest> Lines { get; init; } = [];
}
public sealed class UpdateSalesOrderRequest : CreateSalesOrderRequest
{
    [Required, RowVersion] public string RowVersion { get; init; } = "";
}
public sealed class CancelSalesOrderRequest
{
    [Required, RowVersion] public string RowVersion { get; init; } = "";
}
public sealed class SalesOrderLineRequest
{
    [Range(1, int.MaxValue)] public int ItemId { get; init; }
    public decimal Quantity { get; init; }
    [Price] public decimal UnitPrice { get; init; }
}
public sealed class SalesOrderQuery : IValidatableObject
{
    [Range(1, 1000000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [Range(1, int.MaxValue)] public int? CustomerId { get; init; }
    [EnumDataType(typeof(DocumentStatus))] public DocumentStatus? Status { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    [Required, RegularExpression("^(id|orderDate)$")] public string SortBy { get; init; } = "orderDate";
    [Required, RegularExpression("^(asc|desc)$")] public string SortDirection { get; init; } = "desc";
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (From.HasValue && To.HasValue && From >= To) yield return new("Khoảng ngày phải có from < to.");
    }
}
public sealed record SalesOrderLineResponse(int Id, int LineNumber, int ItemId, decimal Quantity, decimal UnitPrice, decimal LineTotal);
public sealed record SalesOrderSummary(int Id, string OrderNumber, int CustomerId, DateTimeOffset OrderDate,
    DocumentStatus Status, decimal TotalAmount, string? Note, DateTimeOffset CreatedAt, string CreatedByUserId,
    DateTimeOffset? UpdatedAt, string? UpdatedByUserId, DateTimeOffset? CancelledAt, string? CancelledByUserId, string RowVersion,
    DateTimeOffset? ConfirmedAt = null, string? ConfirmedByUserId = null);
public sealed record SalesOrderResponse(int Id, string OrderNumber, int CustomerId, DateTimeOffset OrderDate,
    DocumentStatus Status, decimal TotalAmount, string? Note, DateTimeOffset CreatedAt, string CreatedByUserId,
    DateTimeOffset? UpdatedAt, string? UpdatedByUserId, DateTimeOffset? CancelledAt, string? CancelledByUserId,
    string RowVersion, IReadOnlyList<SalesOrderLineResponse> Lines,
    DateTimeOffset? ConfirmedAt = null, string? ConfirmedByUserId = null);
public sealed record SalesOrderPage(IReadOnlyList<SalesOrderSummary> Items, int Page, int PageSize, int TotalCount);
