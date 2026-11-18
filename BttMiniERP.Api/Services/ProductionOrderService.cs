using BttMiniERP.Api.Common;
using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.DTOs.ProductionOrders;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Api.Services;

public class ProductionOrderService(ApplicationDbContext db, CurrentUser actor, TimeProvider clock, InventoryService inventory)
{
    public virtual async Task<ProductionOrderPage> ListAsync(ProductionOrderQuery query, CancellationToken ct)
    {
        MasterDataRules.Validate(query);
        var source = db.ProductionOrders.AsNoTracking();
        if (query.BomId.HasValue) source = source.Where(x => x.BomId == query.BomId);
        if (query.FinishedProductId.HasValue) source = source.Where(x => x.FinishedProductId == query.FinishedProductId);
        if (query.Status.HasValue) source = source.Where(x => x.Status == query.Status);
        if (query.From.HasValue) source = source.Where(x => x.CreatedAt >= query.From.Value.ToUniversalTime());
        if (query.To.HasValue) source = source.Where(x => x.CreatedAt < query.To.Value.ToUniversalTime());
        var count = await source.CountAsync(ct);
        var ordered = (query.SortBy, query.SortDirection) switch {
            ("id", "asc") => source.OrderBy(x => x.Id), ("id", "desc") => source.OrderByDescending(x => x.Id),
            ("createdAt", "asc") => source.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            ("createdAt", "desc") => source.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id),
            _ => throw new ApiException(400, "Sắp xếp không hợp lệ.") };
        var rows = await ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(ct);
        return new(rows.Select(Summary).ToArray(), query.Page, query.PageSize, count);
    }

    public virtual async Task<ProductionOrderResponse> GetAsync(int id, CancellationToken ct)
        => Response(await db.ProductionOrders.AsNoTracking().Include(x => x.Materials).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new ApiException(404, "Không tìm thấy lệnh sản xuất."));

    public virtual async Task<ProductionOrderResponse> CreateAsync(CreateProductionOrderRequest request, CancellationToken ct)
    {
        MasterDataRules.Validate(request);
        ProductionOrderRules.RequireQuantity(request.Quantity);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Cùng khóa header với sửa/vô hiệu BOM: không chụp header mới và details cũ.
        var bom = await db.BillOfMaterials.FromSqlInterpolated(
            $"SELECT * FROM [BillOfMaterials] WITH (UPDLOCK, HOLDLOCK) WHERE [Id]={request.BomId}")
            .AsNoTracking().SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "Không tìm thấy BOM.");
        bom.Details = await db.BillOfMaterialDetails.AsNoTracking().Where(x => x.BillOfMaterialId == bom.Id).ToListAsync(ct);
        var materials = ProductionOrderRules.Snapshot(bom, request.Quantity);
        foreach (var id in materials.Select(x => x.RawMaterialId).Append(bom.FinishedProductId).Distinct().Order())
        {
            var item = await db.Items.FromSqlInterpolated($"SELECT * FROM [Items] WITH (UPDLOCK, HOLDLOCK) WHERE [Id]={id}")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            BillOfMaterialRules.RequireItem(item, id == bom.FinishedProductId ? ItemType.FinishedProduct : ItemType.RawMaterial);
        }
        var order = new ProductionOrder { OrderNumber = DocumentNumberGenerator.NewProductionOrder(), BomId = bom.Id,
            BomVersionSnapshot = bom.Version, FinishedProductId = bom.FinishedProductId, Quantity = request.Quantity,
            Note = request.Note?.Trim(), Status = DocumentStatus.Draft, CreatedAt = clock.GetUtcNow(), CreatedByUserId = actor.Id,
            Materials = materials.ToList() };
        db.ProductionOrders.Add(order);
        await Save(ct);
        await tx.CommitAsync(ct);
        return Response(order);
    }

    public virtual async Task<ProductionOrderResponse> UpdateAsync(int id, UpdateProductionOrderRequest request, CancellationToken ct)
    {
        MasterDataRules.Validate(request);
        ProductionOrderRules.RequireQuantity(request.Quantity);
        var version = MasterDataRules.ParseVersion(request.RowVersion);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await LockDraft(id, version, ct);
        // Không đọc lại BOM, không thay danh sách NVL hoặc định mức đã chụp.
        var required = order.Materials.Select(x => ProductionOrderRules.RequiredQuantity(x.QuantityPerUnitSnapshot, request.Quantity)).ToArray();
        for (var i = 0; i < order.Materials.Count; i++) order.Materials[i].RequiredQuantity = required[i];
        order.Quantity = request.Quantity;
        order.Note = request.Note?.Trim();
        order.UpdatedAt = clock.GetUtcNow(); order.UpdatedByUserId = actor.Id;
        db.Entry(order).Property(x => x.UpdatedAt).IsModified = true;
        await Save(ct);
        await tx.CommitAsync(ct);
        return Response(order);
    }

    public virtual async Task<ProductionOrderResponse> CancelAsync(int id, string rowVersion, CancellationToken ct)
    {
        var version = MasterDataRules.ParseVersion(rowVersion);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await LockDraft(id, version, ct);
        order.Status = DocumentStatus.Cancelled;
        order.CancelledAt = order.UpdatedAt = clock.GetUtcNow();
        order.CancelledByUserId = order.UpdatedByUserId = actor.Id;
        await Save(ct);
        await tx.CommitAsync(ct);
        return Response(order);
    }

    public virtual async Task<ProductionOrderResponse> ConfirmAsync(int id, string rowVersion, CancellationToken ct)
    {
        var version = MasterDataRules.ParseVersion(rowVersion);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await LockDraft(id, version, ct);
        var movements = ProductionOrderRules.ConfirmationMovements(order);
        await inventory.ApplyAsync(movements, ct);

        foreach (var material in order.Materials)
            material.ActualQuantity = material.RequiredQuantity;
        order.Status = DocumentStatus.Confirmed;
        order.ConfirmedAt = order.UpdatedAt = clock.GetUtcNow();
        order.ConfirmedByUserId = order.UpdatedByUserId = actor.Id;
        await Save(ct);
        await transaction.CommitAsync(ct);
        return Response(order);
    }

    private async Task<ProductionOrder> LockDraft(int id, byte[] version, CancellationToken ct)
    {
        var order = await db.ProductionOrders.FromSqlInterpolated(
            $"SELECT * FROM [ProductionOrders] WITH (UPDLOCK, HOLDLOCK) WHERE [Id]={id}")
            .SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "Không tìm thấy lệnh sản xuất.");
        await db.Entry(order).ReloadAsync(ct);
        ProductionOrderRules.RequireDraft(order.Status);
        if (!order.RowVersion.SequenceEqual(version)) throw new ApiException(409, "Lệnh đã thay đổi; tải lại RowVersion.");
        db.Entry(order).Property(x => x.RowVersion).OriginalValue = version;
        order.Materials = await db.ProductionOrderMaterials.Where(x => x.ProductionOrderId == id).ToListAsync(ct);
        if (order.Materials.Count == 0 || order.Materials.Any(x => x.ActualQuantity != null))
            throw new ApiException(409, "Snapshot lệnh Draft không hợp lệ.");
        return order;
    }

    private async Task Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ApiException(409, "Lệnh đã thay đổi; tải lại RowVersion."); }
        catch (DbUpdateException e) when (e.InnerException is SqlException sql && sql.Number is 2601 or 2627
            && sql.Message.Contains("IX_ProductionOrders_OrderNumber", StringComparison.Ordinal))
        { throw new ApiException(409, "Mã lệnh sản xuất bị trùng; hãy tạo lại yêu cầu."); }
    }

    private static ProductionOrderSummary Summary(ProductionOrder x) => new(x.Id, x.OrderNumber, x.BomId, x.BomVersionSnapshot,
        x.FinishedProductId, x.Quantity, x.Status, x.Note, x.CreatedAt, x.CreatedByUserId, x.UpdatedAt, x.UpdatedByUserId,
        x.CancelledAt, x.CancelledByUserId, x.ConfirmedAt, x.ConfirmedByUserId, Convert.ToBase64String(x.RowVersion));
    private static ProductionOrderResponse Response(ProductionOrder x) => new(x.Id, x.OrderNumber, x.BomId, x.BomVersionSnapshot,
        x.FinishedProductId, x.Quantity, x.Status, x.Note, x.CreatedAt, x.CreatedByUserId, x.UpdatedAt, x.UpdatedByUserId,
        x.CancelledAt, x.CancelledByUserId, x.ConfirmedAt, x.ConfirmedByUserId, Convert.ToBase64String(x.RowVersion),
        x.Materials.OrderBy(m => m.RawMaterialId).Select(m => new ProductionOrderMaterialResponse(m.Id, m.RawMaterialId,
            m.QuantityPerUnitSnapshot, m.RequiredQuantity, m.ActualQuantity)).ToArray());
}
