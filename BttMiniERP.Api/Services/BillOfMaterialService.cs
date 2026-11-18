using BttMiniERP.Api.Common;
using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.DTOs.BillOfMaterials;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Api.Services;

public class BillOfMaterialService(ApplicationDbContext db, CurrentUser actor, TimeProvider clock)
{
    public virtual async Task<BillOfMaterialPage> ListAsync(BillOfMaterialQuery query, CancellationToken ct)
    {
        MasterDataRules.Validate(query);
        var source = db.BillOfMaterials.AsNoTracking();
        if (query.IsActive.HasValue) source = source.Where(x => x.IsActive == query.IsActive);
        if (query.FinishedProductId.HasValue) source = source.Where(x => x.FinishedProductId == query.FinishedProductId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var code = query.Search.Trim().ToUpperInvariant();
            source = source.Where(x => x.Code.Contains(code));
        }
        var count = await source.CountAsync(ct);
        var ordered = (query.SortBy, query.SortDirection) switch
        {
            ("id", "asc") => source.OrderBy(x => x.Id), ("id", "desc") => source.OrderByDescending(x => x.Id),
            ("code", "asc") => source.OrderBy(x => x.Code).ThenBy(x => x.Id),
            ("code", "desc") => source.OrderByDescending(x => x.Code).ThenByDescending(x => x.Id),
            _ => throw new ApiException(400, "Sắp xếp BOM không hợp lệ.")
        };
        var rows = await ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(ct);
        return new(rows.Select(Summary).ToArray(), query.Page, query.PageSize, count);
    }

    public virtual async Task<BillOfMaterialResponse> GetAsync(int id, CancellationToken ct)
        => Response(await db.BillOfMaterials.AsNoTracking().Include(x => x.Details).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new ApiException(404, "Không tìm thấy BOM."));

    public virtual async Task<BillOfMaterialResponse> CreateAsync(CreateBillOfMaterialRequest request, CancellationToken ct)
    {
        var details = BillOfMaterialRules.Details(request);
        var code = MasterDataRules.NormalizeCode(request.Code);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ValidateItems(request.FinishedProductId, details, ct);
        await RequireUnique(code, request.FinishedProductId, 0, ct);
        var bom = new BillOfMaterial { Code = code, FinishedProductId = request.FinishedProductId, Version = 1,
            IsActive = request.IsActive, Note = request.Note?.Trim(), CreatedAt = clock.GetUtcNow(), CreatedByUserId = actor.Id,
            Details = details.ToList() };
        db.BillOfMaterials.Add(bom);
        await Save(ct); await tx.CommitAsync(ct);
        return Response(bom);
    }

    public virtual async Task<BillOfMaterialResponse> UpdateAsync(int id, UpdateBillOfMaterialRequest request, CancellationToken ct)
    {
        var details = BillOfMaterialRules.Details(request);
        var version = MasterDataRules.ParseVersion(request.RowVersion);
        var code = MasterDataRules.NormalizeCode(request.Code);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var bom = await Lock(id, version, ct);
        var next = BillOfMaterialRules.NextVersion(bom.Version);
        // P4.1 chưa có ProductionOrders. P4.2 phải kiểm tra tham chiếu trước đổi FinishedProductId
        // và dùng snapshot riêng; không dùng Details đang thay thế làm lịch sử sản xuất.
        await ValidateItems(request.FinishedProductId, details, ct);
        await RequireUnique(code, request.FinishedProductId, id, ct);
        db.BillOfMaterialDetails.RemoveRange(bom.Details);
        bom.Code = code; bom.FinishedProductId = request.FinishedProductId;
        bom.IsActive = request.IsActive; bom.Note = request.Note?.Trim(); bom.Version = next;
        SetUpdated(bom);
        await Save(ct); // Xóa dòng cũ trước INSERT để tránh va chạm unique tạm.
        bom.Details = details.ToList();
        await Save(ct); await tx.CommitAsync(ct);
        return Response(bom);
    }

    public virtual async Task<BillOfMaterialResponse> DeactivateAsync(int id, string rowVersion, CancellationToken ct)
    {
        var version = MasterDataRules.ParseVersion(rowVersion);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var bom = await Lock(id, version, ct);
        BillOfMaterialRules.RequireActiveForDeactivation(bom.IsActive);
        bom.Version = BillOfMaterialRules.NextVersion(bom.Version);
        bom.IsActive = false; SetUpdated(bom);
        await Save(ct); await tx.CommitAsync(ct);
        return Response(bom);
    }

    private async Task<BillOfMaterial> Lock(int id, byte[] version, CancellationToken ct)
    {
        var bom = await db.BillOfMaterials.FromSqlInterpolated(
            $"SELECT * FROM [BillOfMaterials] WITH (UPDLOCK, HOLDLOCK) WHERE [Id]={id}")
            .SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "Không tìm thấy BOM.");
        await db.Entry(bom).ReloadAsync(ct);
        if (!bom.RowVersion.SequenceEqual(version)) throw new ApiException(409, "BOM đã thay đổi; tải lại RowVersion.");
        db.Entry(bom).Property(x => x.RowVersion).OriginalValue = version;
        bom.Details = await db.BillOfMaterialDetails.Where(x => x.BillOfMaterialId == id).ToListAsync(ct);
        return bom;
    }

    private async Task ValidateItems(int productId, IEnumerable<BillOfMaterialDetail> details, CancellationToken ct)
    {
        // Khóa cả đầu ra và đầu vào theo cùng ItemId; cùng quy ước với ItemService.
        foreach (var id in details.Select(x => x.RawMaterialId).Append(productId).Distinct().Order())
        {
            var item = await db.Items.FromSqlInterpolated($"SELECT * FROM [Items] WITH (UPDLOCK, HOLDLOCK) WHERE [Id]={id}")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            BillOfMaterialRules.RequireItem(item, id == productId ? ItemType.FinishedProduct : ItemType.RawMaterial);
        }
    }

    private async Task RequireUnique(string code, int productId, int id, CancellationToken ct)
    {
        if (await db.BillOfMaterials.AnyAsync(x => x.Id != id && (x.Code == code || x.FinishedProductId == productId), ct))
            throw new ApiException(409, "Mã BOM hoặc thành phẩm đã có BOM (kể cả BOM vô hiệu). ");
    }

    private void SetUpdated(BillOfMaterial bom)
    { bom.UpdatedAt = clock.GetUtcNow(); bom.UpdatedByUserId = actor.Id; }

    private async Task Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ApiException(409, "BOM đã thay đổi; tải lại RowVersion."); }
        catch (DbUpdateException e) when (e.InnerException is SqlException sql && sql.Number is 2601 or 2627
            && (sql.Message.Contains("IX_BillOfMaterials_Code", StringComparison.Ordinal)
                || sql.Message.Contains("IX_BillOfMaterials_FinishedProductId", StringComparison.Ordinal)))
        { throw new ApiException(409, "Mã BOM hoặc thành phẩm đã có BOM."); }
    }

    private static BillOfMaterialSummary Summary(BillOfMaterial x) => new(x.Id, x.Code, x.FinishedProductId, x.Version,
        x.IsActive, x.Note, x.CreatedAt, x.CreatedByUserId, x.UpdatedAt, x.UpdatedByUserId, Convert.ToBase64String(x.RowVersion));
    private static BillOfMaterialResponse Response(BillOfMaterial x) => new(x.Id, x.Code, x.FinishedProductId, x.Version,
        x.IsActive, x.Note, x.CreatedAt, x.CreatedByUserId, x.UpdatedAt, x.UpdatedByUserId, Convert.ToBase64String(x.RowVersion),
        x.Details.OrderBy(d => d.RawMaterialId).Select(d => new BillOfMaterialDetailResponse(d.Id, d.RawMaterialId, d.QuantityPerUnit)).ToArray());
}
