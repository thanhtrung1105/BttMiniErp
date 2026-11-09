using System.ComponentModel.DataAnnotations;
using BttMiniERP.Api.DTOs.MasterData;

namespace BttMiniERP.Api.DTOs.Suppliers;

public class CreateSupplierRequest
{
    [Required, StringLength(50)] public string Code { get; init; } = "";
    [Required, StringLength(200)] public string Name { get; init; } = "";
    [StringLength(30)] public string? Phone { get; init; }
    [StringLength(254), EmailAddress] public string? Email { get; init; }
    [StringLength(500)] public string? Address { get; init; }
    [StringLength(50)] public string? TaxCode { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class UpdateSupplierRequest : CreateSupplierRequest
{
    [Required, RowVersion] public string RowVersion { get; init; } = "";
}

public sealed record SupplierResponse(int Id, string Code, string Name, string? Phone, string? Email, string? Address, string? TaxCode, bool IsActive,
    DateTimeOffset CreatedAt, string CreatedByUserId, DateTimeOffset? UpdatedAt, string? UpdatedByUserId, string RowVersion);
public sealed record SupplierPage(IReadOnlyList<SupplierResponse> Items, int Page, int PageSize, int TotalCount);
