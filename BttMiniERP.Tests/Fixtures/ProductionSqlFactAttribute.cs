namespace BttMiniERP.Tests.Fixtures;

public sealed class ProductionSqlFactAttribute : FactAttribute
{
    public ProductionSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BTT_RUN_SQL_TESTS") != "1"
            || Environment.GetEnvironmentVariable("BTT_RUN_PRODUCTION_SQL_TESTS") != "1")
            Skip = "P4.2 cần phê duyệt localhost/BttMiniERP_ProductionTest và opt-in riêng.";
    }
}
