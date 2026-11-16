namespace BttMiniERP.Tests.Fixtures;

public sealed class PurchasingSqlFactAttribute : FactAttribute
{
    public PurchasingSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BTT_RUN_SQL_TESTS") != "1" || Environment.GetEnvironmentVariable("BTT_RUN_PURCHASING_SQL_TESTS") != "1")
            Skip = "P3.2 cần phê duyệt PurchasingTest và opt-in riêng; chưa chạy SQL.";
    }
}
