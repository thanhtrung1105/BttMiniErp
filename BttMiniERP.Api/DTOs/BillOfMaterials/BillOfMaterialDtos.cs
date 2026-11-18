using System.ComponentModel.DataAnnotations;
using BttMiniERP.Api.DTOs.MasterData;

namespace BttMiniERP.Api.DTOs.BillOfMaterials;

public class CreateBillOfMaterialRequest
{
    [Required, StringLength(50)] public string Code { get; init; } = "";
    [Range(1, int.MaxValue)] public int FinishedProductId { get; init; }
    public bool IsActive { get; init; } = true;
    [StringLength(1000)] public string? Note { get; init; }
    [Required, MinLength(1)] public List<BillOfMaterialDetailRequest> Details { get; init; } = [];
}

public sealed class UpdateBillOfMaterialRequest : CreateBillOfMaterialRequest
{
    [Required, RowVersion] public string RowVersion { get; init; } = "";
}

public sealed class BillOfMaterialDetailRequest
{
    [Range(1, int.MaxValue)] public int RawMaterialId { get; init; }
    public decimal QuantityPerUnit { get; init; }
}

public sealed class BillOfMaterialQuery
{
    [Range(1, 1000000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [StringLength(200)] public string? Search { get; init; }
    public bool? IsActive { get; init; }
    [Range(1, int.MaxValue)] public int? FinishedProductId { get; init; }
    [Required, RegularExpression("^(id|code)$")] public string SortBy { get; init; } = "code";
    [Required, RegularExpression("^(asc|desc)$")] public string SortDirection { get; init; } = "asc";
}

public sealed record BillOfMaterialDetailResponse(int Id, int RawMaterialId, decimal QuantityPerUnit);
public sealed record BillOfMaterialSummary(int Id, string Code, int FinishedProductId, int Version, bool IsActive,
    string? Note, DateTimeOffset CreatedAt, string CreatedByUserId, DateTimeOffset? UpdatedAt, string? UpdatedByUserId, string RowVersion);
public sealed record BillOfMaterialResponse(int Id, string Code, int FinishedProductId, int Version, bool IsActive,
    string? Note, DateTimeOffset CreatedAt, string CreatedByUserId, DateTimeOffset? UpdatedAt, string? UpdatedByUserId,
    string RowVersion, IReadOnlyList<BillOfMaterialDetailResponse> Details);
public sealed record BillOfMaterialPage(IReadOnlyList<BillOfMaterialSummary> Items, int Page, int PageSize, int TotalCount);
