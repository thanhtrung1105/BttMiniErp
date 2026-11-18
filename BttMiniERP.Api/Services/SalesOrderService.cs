using BttMiniERP.Api.Common;
using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.DTOs.SalesOrders;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
namespace BttMiniERP.Api.Services;

public class SalesOrderService(ApplicationDbContext db, CurrentUser actor, TimeProvider clock, InventoryService inventory)
{
    public virtual async Task<SalesOrderPage> ListAsync(SalesOrderQuery query, CancellationToken ct)
    {
        SalesOrderRules.Validate(query);
        var source = db.SalesOrders.AsNoTracking();
        if (query.CustomerId.HasValue) source = source.Where(x => x.CustomerId == query.CustomerId);
        if (query.Status.HasValue) source = source.Where(x => x.Status == query.Status);
        if (query.From.HasValue) source = source.Where(x => x.OrderDate >= query.From.Value.ToUniversalTime());
        if (query.To.HasValue) source = source.Where(x => x.OrderDate < query.To.Value.ToUniversalTime());
        var count = await source.CountAsync(ct);
        var ordered = (query.SortBy, query.SortDirection) switch
        {
            ("id", "asc") => source.OrderBy(x => x.Id),
            ("id", "desc") => source.OrderByDescending(x => x.Id),
            ("orderDate", "asc") => source.OrderBy(x => x.OrderDate).ThenBy(x => x.Id),
            ("orderDate", "desc") => source.OrderByDescending(x => x.OrderDate).ThenByDescending(x => x.Id),
            _ => throw new ApiException(400, "Sắp xếp không hợp lệ.")
        };
        var rows = await ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(ct);
        return new(rows.Select(Summary).ToArray(), query.Page, query.PageSize, count);
    }
    public virtual async Task<SalesOrderResponse> GetAsync(int id, CancellationToken ct)
        => Response(await db.SalesOrders.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new ApiException(404, "Không tìm thấy đơn bán."));

    public virtual async Task<SalesOrderResponse> CreateAsync(CreateSalesOrderRequest request, CancellationToken ct)
    {
        var lines = SalesOrderRules.Calculate(request);
        var actorId = actor.Id;
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ValidateReferences(request.CustomerId, lines, ct);
        var order = new SalesOrder
        {
            OrderNumber = DocumentNumberGenerator.NewSalesOrder(),
            CustomerId = request.CustomerId,
            OrderDate = request.OrderDate?.ToUniversalTime() ?? now,
            Note = request.Note?.Trim(),
            Status = DocumentStatus.Draft,
            TotalAmount = lines.Sum(x => x.LineTotal),
            CreatedAt = now,
            CreatedByUserId = actorId,
            Lines = lines.ToList()
        };
        db.SalesOrders.Add(order);
        await Save(ct);
        await tx.CommitAsync(ct);
        return Response(order);
    }
    public virtual async Task<SalesOrderResponse> UpdateAsync(int id, UpdateSalesOrderRequest request, CancellationToken ct)
    {
        var lines = SalesOrderRules.Calculate(request);
        var version = MasterDataRules.ParseVersion(request.RowVersion);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await LockOrder(id, version, ct);
        SalesOrderRules.RequireDraft(order.Status);
        await ValidateReferences(request.CustomerId, lines, ct);
        // Xóa dòng Draft cũ trước INSERT dòng mới để unique (order,item/line) không xung đột tạm.
        // Cả hai SaveChanges ở cùng transaction: lỗi INSERT rollback cả header và DELETE.
        db.SalesOrderLines.RemoveRange(order.Lines);
        order.CustomerId = request.CustomerId;
        order.Note = request.Note?.Trim();
        order.OrderDate = request.OrderDate?.ToUniversalTime() ?? order.OrderDate;
        order.TotalAmount = lines.Sum(x => x.LineTotal);
        order.UpdatedAt = clock.GetUtcNow();
        order.UpdatedByUserId = actor.Id;
        db.Entry(order).Property(x => x.UpdatedAt).IsModified = true;
        await Save(ct);
        order.Lines = lines.ToList();
        await Save(ct);
        await tx.CommitAsync(ct);
        return Response(order);
    }
    public virtual async Task<SalesOrderResponse> CancelAsync(int id, string rowVersion, CancellationToken ct)
    {
        var version = MasterDataRules.ParseVersion(rowVersion);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await LockOrder(id, version, ct);
        SalesOrderRules.Cancel(order, actor.Id, clock.GetUtcNow());
        await Save(ct);
        await tx.CommitAsync(ct);
        return Response(order);
    }
    public virtual async Task<SalesOrderResponse> ConfirmAsync(int id, string rowVersion, CancellationToken ct)
    {
        var version = MasterDataRules.ParseVersion(rowVersion);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await LockOrder(id, version, ct);
        var movements = SalesOrderRules.ConfirmationMovements(order);
        await ValidateCustomer(order.CustomerId, ct);
        await inventory.ApplyAsync(movements, ct);

        order.Status = DocumentStatus.Confirmed;
        order.ConfirmedByUserId = order.UpdatedByUserId = actor.Id;
        order.ConfirmedAt = order.UpdatedAt = clock.GetUtcNow();
        await Save(ct);
        await tx.CommitAsync(ct);
        return Response(order);
    }

    private async Task<SalesOrder> LockOrder(int id, byte[] version, CancellationToken ct)
    {
        var order = await db.SalesOrders.FromSqlInterpolated(
            $"SELECT * FROM [SalesOrders] WITH (UPDLOCK, HOLDLOCK) WHERE [Id]={id}")
            .SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "Không tìm thấy đơn bán.");
        // Reload tránh snapshot cũ nếu caller tái sử dụng scoped context sau request trước.
        await db.Entry(order).ReloadAsync(ct);
        SalesOrderRules.RequireVersion(order, version);
        db.Entry(order).Property(x => x.RowVersion).OriginalValue = version;
        order.Lines = await db.SalesOrderLines.Where(x => x.SalesOrderId == id).ToListAsync(ct);
        return order;
    }
    private async Task ValidateCustomer(int customerId, CancellationToken ct)
    {
        var customer = await db.Customers.FromSqlInterpolated(
            $"SELECT * FROM [Customers] WITH (HOLDLOCK) WHERE [Id]={customerId}").AsNoTracking().SingleOrDefaultAsync(ct);
        SalesOrderRules.RequireCustomer(customer);
    }
    private async Task ValidateReferences(int customerId, IEnumerable<SalesOrderLine> lines, CancellationToken ct)
    {
        await ValidateCustomer(customerId, ct);
        foreach (var id in lines.Select(x => x.ItemId).Order())
        {
            var item = await db.Items.FromSqlInterpolated($"SELECT * FROM [Items] WITH (UPDLOCK, HOLDLOCK) WHERE [Id]={id}")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            SalesOrderRules.RequireProduct(item);
        }
    }
    private async Task Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ApiException(409, "Đơn đã thay đổi; tải lại RowVersion."); }
        catch (DbUpdateException e) when (e.InnerException is SqlException sql && sql.Number is 2601 or 2627
            && sql.Message.Contains("IX_SalesOrders_OrderNumber", StringComparison.Ordinal))
        { throw new ApiException(409, "Mã đơn trùng; hãy tạo lại đơn."); }
    }
    private static SalesOrderSummary Summary(SalesOrder x) => new(x.Id, x.OrderNumber, x.CustomerId, x.OrderDate,
        x.Status, x.TotalAmount, x.Note, x.CreatedAt, x.CreatedByUserId, x.UpdatedAt, x.UpdatedByUserId,
        x.CancelledAt, x.CancelledByUserId, Convert.ToBase64String(x.RowVersion), x.ConfirmedAt, x.ConfirmedByUserId);
    private static SalesOrderResponse Response(SalesOrder x) => new(x.Id, x.OrderNumber, x.CustomerId, x.OrderDate,
        x.Status, x.TotalAmount, x.Note, x.CreatedAt, x.CreatedByUserId, x.UpdatedAt, x.UpdatedByUserId,
        x.CancelledAt, x.CancelledByUserId, Convert.ToBase64String(x.RowVersion),
        x.Lines.OrderBy(l => l.LineNumber).Select(l => new SalesOrderLineResponse(l.Id, l.LineNumber, l.ItemId, l.Quantity, l.UnitPrice, l.LineTotal)).ToArray(), x.ConfirmedAt, x.ConfirmedByUserId);
}
