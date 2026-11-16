using BttMiniERP.Api.Common;
using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.DTOs.PurchaseReceipts;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
namespace BttMiniERP.Api.Services;

public class PurchaseReceiptService(ApplicationDbContext db, CurrentUser actor, TimeProvider clock, InventoryService inventory)
{
    public virtual async Task<PurchaseReceiptPage> ListAsync(PurchaseReceiptQuery query, CancellationToken ct)
    {
        PurchaseReceiptRules.Validate(query);
        var source = db.PurchaseReceipts.AsNoTracking();
        if (query.SupplierId.HasValue) source = source.Where(x => x.SupplierId == query.SupplierId);
        if (query.Status.HasValue) source = source.Where(x => x.Status == query.Status);
        if (query.From.HasValue) source = source.Where(x => x.ReceiptDate >= query.From.Value.ToUniversalTime());
        if (query.To.HasValue) source = source.Where(x => x.ReceiptDate < query.To.Value.ToUniversalTime());
        var count = await source.CountAsync(ct);
        var ordered = (query.SortBy, query.SortDirection) switch {
            ("id", "asc") => source.OrderBy(x => x.Id), ("id", "desc") => source.OrderByDescending(x => x.Id),
            ("receiptDate", "asc") => source.OrderBy(x => x.ReceiptDate).ThenBy(x => x.Id),
            ("receiptDate", "desc") => source.OrderByDescending(x => x.ReceiptDate).ThenByDescending(x => x.Id),
            _ => throw new ApiException(400, "Sắp xếp không hợp lệ.") };
        var rows = await ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(ct);
        return new(rows.Select(Summary).ToArray(), query.Page, query.PageSize, count);
    }
    public virtual async Task<PurchaseReceiptResponse> GetAsync(int id, CancellationToken ct)
        => Response(await db.PurchaseReceipts.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new ApiException(404, "Không tìm thấy phiếu nhập."));

    public virtual async Task<PurchaseReceiptResponse> CreateAsync(CreatePurchaseReceiptRequest request, CancellationToken ct)
    {
        var lines = PurchaseReceiptRules.Calculate(request);
        var actorId = actor.Id; var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ValidateReferences(request.SupplierId, lines, ct);
        var receipt = new PurchaseReceipt { ReceiptNumber = DocumentNumberGenerator.NewPurchaseReceipt(), SupplierId = request.SupplierId,
            ReceiptDate = request.ReceiptDate?.ToUniversalTime() ?? now, Note = request.Note?.Trim(), Status = DocumentStatus.Draft,
            TotalAmount = lines.Sum(x => x.LineTotal), CreatedAt = now, CreatedByUserId = actorId, Lines = lines.ToList() };
        db.PurchaseReceipts.Add(receipt);
        await Save(ct); await tx.CommitAsync(ct);
        return Response(receipt);
    }
    public virtual async Task<PurchaseReceiptResponse> UpdateAsync(int id, UpdatePurchaseReceiptRequest request, CancellationToken ct)
    {
        var lines = PurchaseReceiptRules.Calculate(request);
        var version = MasterDataRules.ParseVersion(request.RowVersion);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var receipt = await LockDraft(id, version, ct);
        await ValidateReferences(request.SupplierId, lines, ct);
        // Xóa dòng Draft cũ trước INSERT dòng mới để unique (receipt,item/line) không xung đột tạm.
        // Cả hai SaveChanges ở cùng transaction: lỗi INSERT rollback cả header và DELETE.
        db.PurchaseReceiptLines.RemoveRange(receipt.Lines);
        receipt.SupplierId = request.SupplierId; receipt.Note = request.Note?.Trim();
        receipt.ReceiptDate = request.ReceiptDate?.ToUniversalTime() ?? receipt.ReceiptDate;
        receipt.TotalAmount = lines.Sum(x => x.LineTotal);
        receipt.UpdatedAt = clock.GetUtcNow(); receipt.UpdatedByUserId = actor.Id;
        db.Entry(receipt).Property(x => x.UpdatedAt).IsModified = true;
        await Save(ct);
        receipt.Lines = lines.ToList();
        await Save(ct); await tx.CommitAsync(ct);
        return Response(receipt);
    }
    public virtual async Task<PurchaseReceiptResponse> CancelAsync(int id, string rowVersion, CancellationToken ct)
    {
        var version = MasterDataRules.ParseVersion(rowVersion);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var receipt = await LockDraft(id, version, ct);
        receipt.Status = DocumentStatus.Cancelled;
        receipt.CancelledAt = receipt.UpdatedAt = clock.GetUtcNow();
        receipt.CancelledByUserId = receipt.UpdatedByUserId = actor.Id;
        await Save(ct); await tx.CommitAsync(ct);
        return Response(receipt);
    }
    public virtual async Task<PurchaseReceiptResponse> ConfirmAsync(int id, string rowVersion, CancellationToken ct)
    {
        var version = MasterDataRules.ParseVersion(rowVersion);
        var actorId = actor.Id;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var receipt = await LockDraft(id, version, ct);
        var movements = PurchaseReceiptRules.ConfirmationMovements(receipt);
        await ValidateReferences(receipt.SupplierId, receipt.Lines, ct);
        await inventory.ApplyAsync(movements, ct);
        PurchaseReceiptRules.MarkConfirmed(receipt, actorId, clock.GetUtcNow());
        await Save(ct);
        await tx.CommitAsync(ct);
        return Response(receipt);
    }
    private async Task<PurchaseReceipt> LockDraft(int id, byte[] version, CancellationToken ct)
    {
        var receipt = await db.PurchaseReceipts.FromSqlInterpolated(
            $"SELECT * FROM [PurchaseReceipts] WITH (UPDLOCK, HOLDLOCK) WHERE [Id]={id}")
            .SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "Không tìm thấy phiếu nhập.");
        // Reload tránh snapshot cũ nếu caller tái sử dụng scoped context sau request trước.
        await db.Entry(receipt).ReloadAsync(ct);
        PurchaseReceiptRules.RequireDraft(receipt.Status);
        PurchaseReceiptRules.RequireVersion(receipt, version);
        db.Entry(receipt).Property(x => x.RowVersion).OriginalValue = version;
        receipt.Lines = await db.PurchaseReceiptLines.Where(x => x.PurchaseReceiptId == id).ToListAsync(ct);
        return receipt;
    }
    private async Task ValidateReferences(int supplierId, IEnumerable<PurchaseReceiptLine> lines, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FromSqlInterpolated(
            $"SELECT * FROM [Suppliers] WITH (HOLDLOCK) WHERE [Id]={supplierId}").AsNoTracking().SingleOrDefaultAsync(ct);
        PurchaseReceiptRules.RequireSupplier(supplier);
        foreach (var id in lines.Select(x => x.ItemId).Order())
        {
            var item = await db.Items.FromSqlInterpolated($"SELECT * FROM [Items] WITH (UPDLOCK, HOLDLOCK) WHERE [Id]={id}")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            PurchaseReceiptRules.RequireMaterial(item);
        }
    }
    private async Task Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ApiException(409, "Phiếu đã thay đổi; tải lại RowVersion."); }
        catch (DbUpdateException e) when (e.InnerException is SqlException sql && sql.Number is 2601 or 2627
            && sql.Message.Contains("IX_PurchaseReceipts_ReceiptNumber", StringComparison.Ordinal))
        { throw new ApiException(409, "Mã phiếu trùng; hãy tạo lại phiếu."); }
    }
    private static PurchaseReceiptSummary Summary(PurchaseReceipt x) => new(x.Id, x.ReceiptNumber, x.SupplierId, x.ReceiptDate,
        x.Status, x.TotalAmount, x.Note, x.CreatedAt, x.CreatedByUserId, x.UpdatedAt, x.UpdatedByUserId,
        x.CancelledAt, x.CancelledByUserId, Convert.ToBase64String(x.RowVersion), x.ConfirmedAt, x.ConfirmedByUserId);
    private static PurchaseReceiptResponse Response(PurchaseReceipt x) => new(x.Id, x.ReceiptNumber, x.SupplierId, x.ReceiptDate,
        x.Status, x.TotalAmount, x.Note, x.CreatedAt, x.CreatedByUserId, x.UpdatedAt, x.UpdatedByUserId,
        x.CancelledAt, x.CancelledByUserId, Convert.ToBase64String(x.RowVersion),
        x.Lines.OrderBy(l => l.LineNumber).Select(l => new PurchaseReceiptLineResponse(l.Id, l.LineNumber, l.ItemId, l.Quantity, l.UnitPrice, l.LineTotal)).ToArray(), x.ConfirmedAt, x.ConfirmedByUserId);
}
