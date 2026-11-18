namespace BttMiniERP.Api.Entities;

public sealed class ProductionOrderMaterial
{
    public int Id { get; set; }
    public int ProductionOrderId { get; set; }
    public int RawMaterialId { get; set; }
    public decimal QuantityPerUnitSnapshot { get; set; }
    public decimal RequiredQuantity { get; set; }
    public decimal? ActualQuantity { get; set; }
}
