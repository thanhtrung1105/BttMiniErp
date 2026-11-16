using BttMiniERP.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BttMiniERP.Api.Data.Configurations;

public sealed class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> builder)
    {
        builder.ToTable("InventoryTransactions", table =>
        {
            table.HasCheckConstraint("CK_InventoryTransactions_Delta", "[QuantityDelta] <> 0");
            table.HasCheckConstraint("CK_InventoryTransactions_BalanceAfter", "[BalanceAfter] >= 0");
            table.HasCheckConstraint("CK_InventoryTransactions_Source", """
                [SourceDocumentId] > 0 AND (
                    ([TransactionType] = 0 AND [SourceDocumentType] = 0 AND [SourceLineId] > 0 AND [QuantityDelta] > 0) OR
                    ([TransactionType] = 1 AND [SourceDocumentType] = 1 AND [SourceLineId] > 0 AND [QuantityDelta] < 0) OR
                    ([TransactionType] = 2 AND [SourceDocumentType] = 1 AND [SourceLineId] = 0 AND [QuantityDelta] > 0) OR
                    ([TransactionType] = 3 AND [SourceDocumentType] = 2 AND [SourceLineId] > 0 AND [QuantityDelta] < 0))
                """);
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.QuantityDelta).HasPrecision(18, 3);
        builder.Property(x => x.BalanceAfter).HasPrecision(18, 3);
        builder.Property(x => x.PerformedByUserId).HasMaxLength(128).IsRequired();
        builder.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.PerformedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => new { x.SourceDocumentType, x.SourceDocumentId, x.SourceLineId, x.TransactionType })
            .IsUnique().HasDatabaseName("IX_InventoryTransactions_Source");
        builder.HasIndex(x => new { x.ItemId, x.OccurredAt, x.Id });
    }
}
