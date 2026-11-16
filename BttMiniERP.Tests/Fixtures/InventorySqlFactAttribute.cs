namespace BttMiniERP.Tests.Fixtures;

public sealed class InventorySqlFactAttribute : FactAttribute
{
    public InventorySqlFactAttribute(bool migration = false)
    {
        var flag = migration ? "BTT_RUN_INVENTORY_MIGRATION_SQL_TESTS" : "BTT_RUN_INVENTORY_SQL_TESTS";
        if (Environment.GetEnvironmentVariable("BTT_RUN_SQL_TESTS") != "1" || Environment.GetEnvironmentVariable(flag) != "1")
            Skip = "P3.1 cần phê duyệt database Inventory riêng và opt-in; chưa chạy SQL.";
    }
}
