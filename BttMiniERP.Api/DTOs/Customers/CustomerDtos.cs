using System.ComponentModel.DataAnnotations;
using BttMiniERP.Api.DTOs.MasterData;

namespace BttMiniERP.Api.DTOs.Customers;

public class CreateCustomerRequest
{
    [Required, StringLength(50)] public string Code { get; init; } = "";
    [Required, StringLength(200)] public string Name { get; init; } = "";
    [StringLength(30)] public string? Phone { get; init; }
    [StringLength(254), EmailAddress] public string? Email { get; init; }
    [StringLength(500)] public string? Address { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class UpdateCustomerRequest : CreateCustomerRequest
{
    [Required, RowVersion] public string RowVersion { get; init; } = "";
}

public sealed record CustomerResponse(int Id, string Code, string Name, string? Phone, string? Email, string? Address, bool IsActive,
    DateTimeOffset CreatedAt, string CreatedByUserId, DateTimeOffset? UpdatedAt, string? UpdatedByUserId, string RowVersion);
public sealed record CustomerPage(IReadOnlyList<CustomerResponse> Items, int Page, int PageSize, int TotalCount);
