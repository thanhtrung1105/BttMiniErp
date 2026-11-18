namespace BttMiniERP.Api.Common;

public static class DocumentNumberGenerator
{
    public static string NewProductionOrder() => "PO-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
    public static string NewSalesOrder() => "SO-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
    public static string NewPurchaseReceipt() => "PR-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
}
