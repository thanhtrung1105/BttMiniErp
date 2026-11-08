using System.ComponentModel.DataAnnotations;
using BttMiniERP.Api.DTOs.MasterData;

namespace BttMiniERP.Api.DTOs.Categories;

public class CreateCategoryRequest
{
    [Required, StringLength(50)] public string Code { get; init; } = "";
    [Required, StringLength(200)] public string Name { get; init; } = "";
    [StringLength(1000)] public string? Description { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class UpdateCategoryRequest : CreateCategoryRequest
{
    [Required, RowVersion] public string RowVersion { get; init; } = "";
}

public sealed record CategoryResponse(int Id, string Code, string Name, string? Description, bool IsActive,
    DateTimeOffset CreatedAt, string CreatedByUserId, DateTimeOffset? UpdatedAt, string? UpdatedByUserId, string RowVersion);
public sealed record CategoryPage(IReadOnlyList<CategoryResponse> Items, int Page, int PageSize, int TotalCount);
