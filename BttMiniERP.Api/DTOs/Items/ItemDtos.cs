using System.ComponentModel.DataAnnotations;
using BttMiniERP.Api.DTOs.MasterData;
using BttMiniERP.Api.Enums;

namespace BttMiniERP.Api.DTOs.Items;

public class CreateItemRequest
{
    [Required, StringLength(50)] public string Code { get; init; } = "";
    [Required, StringLength(200)] public string Name { get; init; } = "";
    [Range(1, int.MaxValue)] public int CategoryId { get; init; }
    [Required, StringLength(20)] public string Unit { get; init; } = "";
    [Required, EnumDataType(typeof(ItemType))] public ItemType? ItemType { get; init; }
    [Price] public decimal PurchasePrice { get; init; }
    [Price] public decimal SalePrice { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class UpdateItemRequest : CreateItemRequest
{
    [Required, RowVersion] public string RowVersion { get; init; } = "";
}

public sealed class ItemQuery : MasterDataQuery
{
    [Range(1, int.MaxValue)] public int? CategoryId { get; init; }
    [EnumDataType(typeof(ItemType))] public ItemType? ItemType { get; init; }
}

public sealed record ItemResponse(int Id, string Code, string Name, int CategoryId, string Unit, ItemType ItemType,
    decimal PurchasePrice, decimal SalePrice, bool IsActive, DateTimeOffset CreatedAt, string CreatedByUserId,
    DateTimeOffset? UpdatedAt, string? UpdatedByUserId, string RowVersion);
public sealed record ItemPage(IReadOnlyList<ItemResponse> Items, int Page, int PageSize, int TotalCount);
