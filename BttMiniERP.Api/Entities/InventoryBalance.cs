namespace BttMiniERP.Api.Entities;

public sealed class InventoryBalance
{
    public int ItemId { get; set; }
    public decimal QuantityOnHand { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
