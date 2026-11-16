using BttMiniERP.Api.Common;
using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Api.Services;

public sealed class InventoryService(ApplicationDbContext context, CurrentUser actor, TimeProvider clock)
{
    // Caller phải gửi toàn bộ các dòng (kể cả đầu ra sản xuất) trong một batch và tự commit/rollback.
    public async Task ApplyAsync(IReadOnlyList<InventoryMovement> movements, CancellationToken cancellationToken)
    {
        var actorId = actor.Id;
        var ordered = InventoryMovement.ValidateAndOrder(movements, actorId);
        var transaction = context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("InventoryService yêu cầu transaction của caller.");
        if (!transaction.SupportsSavepoints)
            throw new InvalidOperationException("InventoryService yêu cầu savepoint; không bật MultipleActiveResultSets.");

        // Không SaveChanges ở đây: tránh flush trạng thái chứng từ đang tracked của caller.
        // Loại bỏ snapshot balance cũ; không âm thầm discard thay đổi chưa lưu của caller.
        var itemIds = ordered.Select(x => x.ItemId).ToHashSet();
        var tracked = context.ChangeTracker.Entries<InventoryBalance>().Where(x => itemIds.Contains(x.Entity.ItemId)).ToArray();
        if (tracked.Any(x => x.State != EntityState.Unchanged))
            throw new InvalidOperationException("Không chỉnh balance qua change tracker trước khi gọi InventoryService.");
        foreach (var entry in tracked) entry.State = EntityState.Detached;

        var savepoint = "Inventory_" + Guid.NewGuid().ToString("N")[..16];
        await transaction.CreateSavepointAsync(savepoint, cancellationToken);
        try
        {
            foreach (var movement in ordered)
            {
                // Cùng khóa Item với ItemService.Update: không đổi loại/đơn vị khi bắt đầu có ledger.
                var item = await context.Items.FromSqlInterpolated(
                    $"SELECT * FROM [Items] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {movement.ItemId}")
                    .AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? throw new ApiException(404, "Không tìm thấy Item.");
                var expectedType = movement.TransactionType is InventoryTransactionType.PurchaseReceipt or InventoryTransactionType.ProductionConsumption
                    ? ItemType.RawMaterial : ItemType.FinishedProduct;
                if (!item.IsActive || item.ItemType != expectedType)
                    throw new ApiException(409, "Item không hoạt động hoặc sai loại cho biến động kho.");

                var now = clock.GetUtcNow();
                var delta = movement.QuantityDelta;
                var minimum = delta < 0 ? -delta : 0m;
                var maximum = delta > 0 ? InventoryMovement.MaximumQuantity - delta : InventoryMovement.MaximumQuantity;
                // Raw SQL phải chỉ rõ decimal(18,3), không dùng mapping decimal mặc định (18,2).
                var deltaParameter = QuantityParameter("@delta", delta);
                var minimumParameter = QuantityParameter("@minimum", minimum);
                var maximumParameter = QuantityParameter("@maximum", maximum);
                var changed = await context.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE [InventoryBalances]
                    SET [QuantityOnHand] = [QuantityOnHand] + {deltaParameter}, [UpdatedAt] = {now}, [UpdatedByUserId] = {actorId}
                    WHERE [ItemId] = {movement.ItemId} AND [QuantityOnHand] >= {minimumParameter} AND [QuantityOnHand] <= {maximumParameter}
                    """, cancellationToken);
                if (changed != 1) throw new ApiException(409, "Không đủ tồn kho, vượt giới hạn số lượng hoặc thiếu balance.");
                var balance = await context.InventoryBalances.AsNoTracking().SingleAsync(x => x.ItemId == movement.ItemId, cancellationToken);
                var ledgerDelta = QuantityParameter("@delta", delta);
                var balanceAfter = QuantityParameter("@balanceAfter", balance.QuantityOnHand);
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [InventoryTransactions]
                        ([ItemId], [TransactionType], [QuantityDelta], [BalanceAfter], [SourceDocumentType], [SourceDocumentId], [SourceLineId], [PerformedByUserId], [OccurredAt])
                    VALUES ({movement.ItemId}, {(int)movement.TransactionType}, {ledgerDelta}, {balanceAfter},
                        {(int)movement.SourceDocumentType}, {movement.SourceDocumentId}, {movement.SourceLineId}, {actorId}, {now})
                    """, cancellationToken);
            }
            // SQL Server giữ savepoint tới khi caller kết thúc transaction; không commit tại đây.
        }
        catch (Exception error)
        {
            try { await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None); }
            catch (Exception rollbackError)
            {
                // Deadlock có thể đã hủy toàn transaction ở SQL Server. Không tiếp tục dùng transaction lỗi.
                try { await transaction.RollbackAsync(CancellationToken.None); }
                catch { throw new AggregateException("Transaction kho không thể phục hồi; caller phải dừng.", error, rollbackError); }
            }
            if (error is SqlException sql && sql.Number is 2601 or 2627 && sql.Message.Contains("IX_InventoryTransactions_Source", StringComparison.Ordinal))
                throw new ApiException(409, "Nguồn này đã ghi lịch sử kho.");
            if (error is SqlException { Number: 1205 }) throw new ApiException(409, "Xung đột kho; hãy thử lại toàn bộ giao dịch.");
            throw;
        }
    }

    private static SqlParameter QuantityParameter(string name, decimal value)
        => new(name, System.Data.SqlDbType.Decimal) { Precision = 18, Scale = 3, Value = value };
}
