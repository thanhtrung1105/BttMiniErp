using BttMiniERP.Api.Enums;
namespace BttMiniERP.Api.Entities;

public sealed class PurchaseReceipt
{
    public int Id { get; set; }
    public string ReceiptNumber { get; set; } = "";
    public int SupplierId { get; set; }
    public DateTimeOffset ReceiptDate { get; set; }
    public string? Note { get; set; }
    public DocumentStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelledByUserId { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? ConfirmedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<PurchaseReceiptLine> Lines { get; set; } = [];
}
