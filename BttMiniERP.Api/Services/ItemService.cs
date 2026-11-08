using BttMiniERP.Api.Common;
using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.DTOs.Items;
using BttMiniERP.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Api.Services;

public class ItemService(ApplicationDbContext context, CurrentUser actor, TimeProvider clock)
{
    public virtual async Task<ItemPage> ListAsync(ItemQuery query, CancellationToken cancellationToken)
    {
        MasterDataRules.Validate(query);
        var source = context.Items.AsNoTracking();
        if (query.IsActive.HasValue) source = source.Where(x => x.IsActive == query.IsActive.Value);
        if (query.CategoryId.HasValue) source = source.Where(x => x.CategoryId == query.CategoryId.Value);
        if (query.ItemType.HasValue) source = source.Where(x => x.ItemType == query.ItemType.Value);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            var code = search.ToUpperInvariant();
            source = source.Where(x => x.Code.Contains(code) || x.Name.Contains(search));
        }
        var total = await source.CountAsync(cancellationToken);
        var ordered = (query.SortBy, query.SortDirection) switch
        {
            ("id", "asc") => source.OrderBy(x => x.Id),
            ("id", "desc") => source.OrderByDescending(x => x.Id),
            ("code", "asc") => source.OrderBy(x => x.Code).ThenBy(x => x.Id),
            ("code", "desc") => source.OrderByDescending(x => x.Code).ThenBy(x => x.Id),
            ("name", "asc") => source.OrderBy(x => x.Name).ThenBy(x => x.Id),
            ("name", "desc") => source.OrderByDescending(x => x.Name).ThenBy(x => x.Id),
            _ => throw new ApiException(400, "Sắp xếp không hợp lệ.")
        };
        var items = await ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken);
        return new(items.Select(ToResponse).ToArray(), query.Page, query.PageSize, total);
    }

    public virtual async Task<ItemResponse> GetAsync(int id, CancellationToken cancellationToken)
        => ToResponse(await context.Items.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw NotFound());

    public virtual async Task<ItemResponse> CreateAsync(CreateItemRequest request, CancellationToken cancellationToken)
    {
        MasterDataRules.Validate(request);
        var code = MasterDataRules.NormalizeCode(request.Code);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await RequireActiveCategoryAsync(request.CategoryId, cancellationToken);
        await EnsureUniqueAsync(code, 0, cancellationToken);
        var entity = new Item { Code = code, Name = request.Name.Trim(), CategoryId = request.CategoryId,
            Unit = request.Unit.Trim(), ItemType = request.ItemType!.Value, PurchasePrice = request.PurchasePrice,
            SalePrice = request.SalePrice, IsActive = request.IsActive, CreatedAt = clock.GetUtcNow(), CreatedByUserId = actor.Id };
        context.Items.Add(entity);
        await MasterDataRules.SaveAsync(context, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToResponse(entity);
    }

    public virtual async Task<ItemResponse> UpdateAsync(int id, UpdateItemRequest request, CancellationToken cancellationToken)
    {
        MasterDataRules.Validate(request);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var entity = await context.Items.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw NotFound();
        context.Entry(entity).Property(x => x.RowVersion).OriginalValue = MasterDataRules.ParseVersion(request.RowVersion);
        // P2.1 chưa có BOM/chứng từ/snapshot/ledger. P3/P4 phải thay false bằng truy vấn
        // tham chiếu có khóa trong transaction trước khi thêm đường tạo tham chiếu nghiệp vụ.
        MasterDataRules.EnsureClassificationChangeAllowed(entity.ItemType, entity.Unit,
            request.ItemType!.Value, request.Unit, hasBusinessReferences: false);
        if (entity.CategoryId != request.CategoryId) await RequireActiveCategoryAsync(request.CategoryId, cancellationToken);
        var code = MasterDataRules.NormalizeCode(request.Code);
        await EnsureUniqueAsync(code, id, cancellationToken);
        entity.Code = code;
        entity.Name = request.Name.Trim();
        entity.CategoryId = request.CategoryId;
        entity.Unit = request.Unit.Trim();
        entity.ItemType = request.ItemType.Value;
        entity.PurchasePrice = request.PurchasePrice;
        entity.SalePrice = request.SalePrice;
        entity.IsActive = request.IsActive;
        SetUpdated(entity);
        await MasterDataRules.SaveAsync(context, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToResponse(entity);
    }

    public virtual async Task<ItemResponse> DeactivateAsync(int id, string version, CancellationToken cancellationToken)
    {
        var bytes = MasterDataRules.ParseVersion(version);
        var entity = await context.Items.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw NotFound();
        context.Entry(entity).Property(x => x.RowVersion).OriginalValue = bytes;
        entity.IsActive = false;
        SetUpdated(entity);
        await MasterDataRules.SaveAsync(context, cancellationToken);
        return ToResponse(entity);
    }

    private async Task RequireActiveCategoryAsync(int id, CancellationToken cancellationToken)
    {
        // Giữ khóa tới commit để Category không bị vô hiệu giữa kiểm tra và gán mới.
        var category = await context.Categories.FromSqlInterpolated(
            $"SELECT * FROM [Categories] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (category == null) throw new ApiException(404, "Không tìm thấy Category.");
        if (!category.IsActive) throw new ApiException(409, "Không được gán Category đã vô hiệu.");
    }
    private void SetUpdated(Item entity)
    {
        entity.UpdatedAt = clock.GetUtcNow();
        entity.UpdatedByUserId = actor.Id;
        context.Entry(entity).Property(x => x.UpdatedAt).IsModified = true;
    }
    private async Task EnsureUniqueAsync(string code, int id, CancellationToken cancellationToken)
    {
        if (await context.Items.AnyAsync(x => x.Code == code && x.Id != id, cancellationToken))
            throw new ApiException(409, "Mã Item đã tồn tại.");
    }
    private static ApiException NotFound() => new(404, "Không tìm thấy Item.");
    private static ItemResponse ToResponse(Item x) => new(x.Id, x.Code, x.Name, x.CategoryId, x.Unit, x.ItemType,
        x.PurchasePrice, x.SalePrice, x.IsActive, x.CreatedAt, x.CreatedByUserId, x.UpdatedAt, x.UpdatedByUserId,
        Convert.ToBase64String(x.RowVersion));
}