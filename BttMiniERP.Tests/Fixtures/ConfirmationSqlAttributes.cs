namespace BttMiniERP.Tests.Fixtures;

public sealed class ConfirmationSqlFactAttribute : FactAttribute
{
    public ConfirmationSqlFactAttribute()
    {
        if (!Enabled) Skip = "P3.3 SQL chưa được duyệt; cần Confirmation profile và opt-in riêng.";
    }
    internal static bool Enabled => Environment.GetEnvironmentVariable("BTT_RUN_SQL_TESTS") == "1"
        && Environment.GetEnvironmentVariable("BTT_RUN_CONFIRMATION_SQL_TESTS") == "1";
}

public sealed class ConfirmationSqlTheoryAttribute : TheoryAttribute
{
    public ConfirmationSqlTheoryAttribute()
    {
        if (!ConfirmationSqlFactAttribute.Enabled) Skip = "P3.3 SQL chưa được duyệt; cần Confirmation profile và opt-in riêng.";
    }
}
