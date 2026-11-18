namespace BttMiniERP.Api.Entities;

public sealed class BillOfMaterialDetail
{
    public int Id { get; set; }
    public int BillOfMaterialId { get; set; }
    public int RawMaterialId { get; set; }
    public decimal QuantityPerUnit { get; set; }
}
