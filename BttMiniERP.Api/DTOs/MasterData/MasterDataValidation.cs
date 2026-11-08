using System.ComponentModel.DataAnnotations;

namespace BttMiniERP.Api.DTOs.MasterData;

public sealed class PriceAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is decimal price
        && price >= 0 && price <= 9999999999999999.99m && ((decimal.GetBits(price)[3] >> 16) & 0xff) <= 2;
    public override string FormatErrorMessage(string name) => $"{name} phải không âm và nằm trong decimal(18,2).";
}

public sealed class RowVersionAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value is not string text || text.Length != 12) return false;
        Span<byte> bytes = stackalloc byte[8];
        return Convert.TryFromBase64String(text, bytes, out var count) && count == 8
            && Convert.ToBase64String(bytes) == text;
    }
    public override string FormatErrorMessage(string name) => "RowVersion phải là base64 của 8 byte.";
}

public class MasterDataQuery
{
    [Range(1, 1000000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [StringLength(200)] public string? Search { get; init; }
    public bool? IsActive { get; init; }
    [Required, RegularExpression("^(id|code|name)$")] public string SortBy { get; init; } = "code";
    [Required, RegularExpression("^(asc|desc)$")] public string SortDirection { get; init; } = "asc";
}
