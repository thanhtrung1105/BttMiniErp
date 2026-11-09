using BttMiniERP.Api.Common;
using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.DTOs.Suppliers;
using BttMiniERP.Api.DTOs.MasterData;
using BttMiniERP.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Api.Services;

public class SupplierService(ApplicationDbContext context, CurrentUser actor, TimeProvider clock)
{
    public virtual async Task<SupplierPage> ListAsync(MasterDataQuery query, CancellationToken cancellationToken)
    {
        MasterDataRules.Validate(query);
        var source = context.Suppliers.AsNoTracking();
        if (query.IsActive.HasValue) source = source.Where(x => x.IsActive == query.IsActive.Value);
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

    public virtual async Task<SupplierResponse> GetAsync(int id, CancellationToken cancellationToken)
        => ToResponse(await context.Suppliers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw NotFound());

    public virtual async Task<SupplierResponse> CreateAsync(CreateSupplierRequest request, CancellationToken cancellationToken)
    {
        MasterDataRules.Validate(request);
        var code = MasterDataRules.NormalizeCode(request.Code);
        await EnsureUniqueAsync(code, 0, cancellationToken);
        var entity = new Supplier { Code = code, Name = request.Name.Trim(), Phone = request.Phone?.Trim(), Email = request.Email?.Trim(), Address = request.Address?.Trim(), TaxCode = request.TaxCode?.Trim(),
            IsActive = request.IsActive, CreatedAt = clock.GetUtcNow(), CreatedByUserId = actor.Id };
        context.Suppliers.Add(entity);
        await MasterDataRules.SaveAsync(context, cancellationToken);
        return ToResponse(entity);
    }

    public virtual async Task<SupplierResponse> UpdateAsync(int id, UpdateSupplierRequest request, CancellationToken cancellationToken)
    {
        MasterDataRules.Validate(request);
        var entity = await context.Suppliers.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw NotFound();
        context.Entry(entity).Property(x => x.RowVersion).OriginalValue = MasterDataRules.ParseVersion(request.RowVersion);
        var code = MasterDataRules.NormalizeCode(request.Code);
        await EnsureUniqueAsync(code, id, cancellationToken);
        entity.Code = code;
        entity.Name = request.Name.Trim();
        entity.Phone = request.Phone?.Trim();
        entity.Email = request.Email?.Trim();
        entity.Address = request.Address?.Trim();
        entity.TaxCode = request.TaxCode?.Trim();
        entity.IsActive = request.IsActive;
        SetUpdated(entity);
        await MasterDataRules.SaveAsync(context, cancellationToken);
        return ToResponse(entity);
    }

    public virtual async Task<SupplierResponse> DeactivateAsync(int id, string version, CancellationToken cancellationToken)
    {
        var bytes = MasterDataRules.ParseVersion(version);
        var entity = await context.Suppliers.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw NotFound();
        context.Entry(entity).Property(x => x.RowVersion).OriginalValue = bytes;
        entity.IsActive = false; // Giữ đối tác để đọc lịch sử, không xóa vật lý.
        SetUpdated(entity);
        await MasterDataRules.SaveAsync(context, cancellationToken);
        return ToResponse(entity);
    }

    private void SetUpdated(Supplier entity)
    {
        entity.UpdatedAt = clock.GetUtcNow();
        entity.UpdatedByUserId = actor.Id;
        // Bảo đảm UPDATE cả khi request không đổi nội dung hoặc clock test đứng yên.
        context.Entry(entity).Property(x => x.UpdatedAt).IsModified = true;
    }
    private async Task EnsureUniqueAsync(string code, int id, CancellationToken cancellationToken)
    {
        if (await context.Suppliers.AnyAsync(x => x.Code == code && x.Id != id, cancellationToken))
            throw new ApiException(409, "Mã Supplier đã tồn tại.");
    }
    private static ApiException NotFound() => new(404, "Không tìm thấy Supplier.");
    private static SupplierResponse ToResponse(Supplier x) => new(x.Id, x.Code, x.Name, x.Phone, x.Email, x.Address, x.TaxCode, x.IsActive,
        x.CreatedAt, x.CreatedByUserId, x.UpdatedAt, x.UpdatedByUserId, Convert.ToBase64String(x.RowVersion));
}