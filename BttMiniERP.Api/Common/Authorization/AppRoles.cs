namespace BttMiniERP.Api.Common.Authorization;

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string ManageUsers = "ManageUsers";
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
        { Admin, "Manager", "PurchasingStaff", "SalesStaff", "ProductionStaff", "WarehouseStaff", "Viewer" });

    public static string[] Validate(IEnumerable<string>? roles)
    {
        if (roles == null) throw new ApiException(400, "Phải cung cấp danh sách role.");
        var values = roles.ToArray();
        if (values.Length > All.Count || values.Any(role => !All.Contains(role, StringComparer.Ordinal))
            || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ApiException(400, "Danh sách role không hợp lệ hoặc bị trùng.");
        return values.Order(StringComparer.Ordinal).ToArray();
    }

    public static void EnsureAdminRemains(bool currentlyAdmin, IReadOnlyCollection<string> nextRoles, int adminCount)
    {
        if (currentlyAdmin && !nextRoles.Contains(Admin) && adminCount <= 1)
            throw new ApiException(409, "Không được bỏ quyền của Admin cuối cùng.");
    }
}
