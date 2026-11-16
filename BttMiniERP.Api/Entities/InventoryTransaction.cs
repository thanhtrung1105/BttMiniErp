using BttMiniERP.Api.Enums;

namespace BttMiniERP.Api.Entities;

public sealed class InventoryTransaction
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public InventoryTransactionType TransactionType { get; set; }
    public decimal QuantityDelta { get; set; }
    public decimal BalanceAfter { get; set; }
    public SourceDocumentType SourceDocumentType { get; set; }
    public int SourceDocumentId { get; set; }
    public int SourceLineId { get; set; }
    public string PerformedByUserId { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
}
