namespace BttMiniERP.Api.Entities;

public sealed class SalesOrderLine
{
    public int Id { get; set; }
    public int SalesOrderId { get; set; }
    public int LineNumber { get; set; }
    public int ItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}
