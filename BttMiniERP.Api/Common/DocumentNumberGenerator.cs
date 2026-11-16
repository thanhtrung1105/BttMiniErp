namespace BttMiniERP.Api.Common;

public static class DocumentNumberGenerator
{
    public static string NewPurchaseReceipt() => "PR-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
}
