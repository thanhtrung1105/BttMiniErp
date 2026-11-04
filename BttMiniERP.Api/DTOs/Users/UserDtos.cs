using System.ComponentModel.DataAnnotations;

namespace BttMiniERP.Api.DTOs.Users;

public sealed class CreateUserRequest
{
    [Required, StringLength(256)] public string UserName { get; init; } = "";
    [Required, StringLength(128, MinimumLength = 12)] public string Password { get; init; } = "";
    [Required, MaxLength(7)] public string[]? Roles { get; init; }
}

public sealed class SetRolesRequest
{
    [Required, MaxLength(7)] public string[]? Roles { get; init; }
}

public sealed class UserQuery
{
    [Range(1, 1000000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [StringLength(128)] public string? Id { get; init; }
    [StringLength(256)] public string? Search { get; init; }
    [StringLength(32)] public string? Role { get; init; }
    [RegularExpression("^(id|userName)$")] public string SortBy { get; init; } = "userName";
    [RegularExpression("^(asc|desc)$")] public string SortDirection { get; init; } = "asc";
}

public sealed record UserResponse(string Id, string UserName, IReadOnlyList<string> Roles);
public sealed record UserPage(IReadOnlyList<UserResponse> Items, int Page, int PageSize, int TotalCount);
