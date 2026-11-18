namespace BttMiniERP.Tests.Fixtures;

public sealed class SalesSqlFactAttribute : FactAttribute
{
    public SalesSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BTT_RUN_SQL_TESTS") != "1" || Environment.GetEnvironmentVariable("BTT_RUN_SALES_SQL_TESTS") != "1")
            Skip = "P5.1 cần phê duyệt SalesTest và opt-in riêng; chưa chạy SQL.";
    }
}
