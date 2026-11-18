namespace BttMiniERP.Tests.Fixtures;

public sealed class BomSqlFactAttribute : FactAttribute
{
    public BomSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BTT_RUN_SQL_TESTS") != "1"
            || Environment.GetEnvironmentVariable("BTT_RUN_BOM_SQL_TESTS") != "1")
            Skip = "P4.1 cần phê duyệt localhost/BttMiniERP_BomTest và opt-in riêng.";
    }
}
