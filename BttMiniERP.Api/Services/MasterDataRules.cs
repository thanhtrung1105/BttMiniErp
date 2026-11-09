using System.ComponentModel.DataAnnotations;
using BttMiniERP.Api.Common;
using BttMiniERP.Api.DTOs.MasterData;
using BttMiniERP.Api.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Api.Services;

// Các quy tắc dùng chung của hai danh mục; không thay thế service hoặc DbContext.
public static class MasterDataRules
{
    public static void Validate(object request)
    {
        if (!Validator.TryValidateObject(request, new ValidationContext(request), [], true))
            throw new ApiException(400, "Dữ liệu danh mục không hợp lệ.");
    }

    public static string NormalizeCode(string code)
    {
        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length is < 1 or > 50) throw new ApiException(400, "Mã danh mục không hợp lệ.");
        return normalized;
    }

    public static byte[] ParseVersion(string version)
    {
        if (!new RowVersionAttribute().IsValid(version)) throw new ApiException(400, "RowVersion không hợp lệ.");
        return Convert.FromBase64String(version);
    }

    public static string ParseIfMatch(string? value)
    {
        if (value is not { Length: 14 } || value[0] != '"' || value[^1] != '"')
            throw new ApiException(400, "If-Match phải chứa RowVersion base64 trong dấu ngoặc kép.");
        var version = value[1..^1];
        _ = ParseVersion(version);
        return version;
    }

    public static void EnsureClassificationChangeAllowed(ItemType previousType, string previousUnit,
        ItemType nextType, string nextUnit, bool hasBusinessReferences)
    {
        if (hasBusinessReferences && (previousType != nextType || previousUnit != nextUnit.Trim()))
            throw new ApiException(409, "Không được đổi loại hoặc đơn vị Item đã sử dụng.");
    }

    public static async Task SaveAsync(DbContext context, CancellationToken cancellationToken)
    {
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new ApiException(409, "Dữ liệu đã thay đổi. Hãy tải lại RowVersion."); }
        catch (DbUpdateException error) when (error.InnerException is SqlException sql && sql.Number is 2601 or 2627
            && (sql.Message.Contains("IX_Categories_Code", StringComparison.Ordinal)
                || sql.Message.Contains("IX_Items_Code", StringComparison.Ordinal)
                || sql.Message.Contains("IX_Suppliers_Code", StringComparison.Ordinal)
                || sql.Message.Contains("IX_Customers_Code", StringComparison.Ordinal)))
        { throw new ApiException(409, "Mã danh mục đã tồn tại."); }
    }
}
