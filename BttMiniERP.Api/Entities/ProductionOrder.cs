using BttMiniERP.Api.Enums;

namespace BttMiniERP.Api.Entities;

public sealed class ProductionOrder
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = "";
    public int BomId { get; set; }
    public int BomVersionSnapshot { get; set; }
    public int FinishedProductId { get; set; }
    public decimal Quantity { get; set; }
    public string? Note { get; set; }
    public DocumentStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelledByUserId { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? ConfirmedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<ProductionOrderMaterial> Materials { get; set; } = [];
}
