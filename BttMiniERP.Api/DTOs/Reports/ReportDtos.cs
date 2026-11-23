using System.ComponentModel.DataAnnotations;
using System.Globalization;
using BttMiniERP.Api.Enums;

namespace BttMiniERP.Api.DTOs.Reports;

public class ReportPeriod : IValidatableObject
{
    [Required] public string? From { get; init; }
    [Required] public string? To { get; init; }
    [Range(1, 1000000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;

    private static readonly string[] Formats = ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd'T'HH:mmK"];

    private static bool Parse(string? value, out DateTimeOffset utc)
    {
        utc = default;
        return value != null && (value.EndsWith('Z') || (value.Length >= 6 && value[^6] is '+' or '-'))
            && DateTimeOffset.TryParseExact(value, Formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal, out utc);
    }

    public (DateTimeOffset From, DateTimeOffset To) Bounds()
    {
        if (!Parse(From, out var start) || !Parse(To, out var end) || start >= end)
            throw new ArgumentException("Kỳ báo cáo không hợp lệ.");
        return (start, end);
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        bool startValid = Parse(From, out var start);
        bool endValid = Parse(To, out var end);
        if (!startValid) yield return new("from phải là thời điểm ISO 8601 có múi giờ.", [nameof(From)]);
        if (!endValid) yield return new("to phải là thời điểm ISO 8601 có múi giờ.", [nameof(To)]);
        if (startValid && endValid && start >= end) yield return new("from phải trước to.", [nameof(From), nameof(To)]);
    }
}

public sealed class PurchaseReportQuery : ReportPeriod
{
    [Range(1, int.MaxValue)] public int? SupplierId { get; init; }
    [Range(1, int.MaxValue)] public int? ItemId { get; init; }
}
public sealed class SalesReportQuery : ReportPeriod
{
    [Range(1, int.MaxValue)] public int? CustomerId { get; init; }
    [Range(1, int.MaxValue)] public int? ItemId { get; init; }
}
public sealed class InventoryReportQuery : ReportPeriod
{
    [Range(1, int.MaxValue)] public int? ItemId { get; init; }
    [EnumDataType(typeof(ItemType))] public ItemType? ItemType { get; init; }
}
public sealed class ProductionReportQuery : ReportPeriod
{
    [Range(1, int.MaxValue)] public int? FinishedProductId { get; init; }
}

public sealed record ReportPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
public sealed record PurchaseRow(DateOnly Date, int SupplierId, long DocumentCount, decimal TotalAmount);
public sealed record SalesRow(DateOnly Date, int CustomerId, long DocumentCount, decimal Revenue);
public sealed record ProductionRow(DateOnly Date, int FinishedProductId, string Unit, long DocumentCount, decimal Quantity);
public sealed record InventoryReportRow(int ItemId, string Unit, decimal Opening, decimal Received, decimal Issued, decimal Closing);
