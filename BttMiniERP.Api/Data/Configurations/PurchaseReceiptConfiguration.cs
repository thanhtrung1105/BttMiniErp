using BttMiniERP.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace BttMiniERP.Api.Data.Configurations;

public sealed class PurchaseReceiptConfiguration : IEntityTypeConfiguration<PurchaseReceipt>
{
    public void Configure(EntityTypeBuilder<PurchaseReceipt> b)
    {
        b.ToTable("PurchaseReceipts", t => {
            t.HasCheckConstraint("CK_PurchaseReceipts_Status", "[Status] IN (0,1,2)");
            t.HasCheckConstraint("CK_PurchaseReceipts_Total", "[TotalAmount] >= 0");
        });
        b.HasKey(x => x.Id); b.Property(x => x.ReceiptNumber).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.ReceiptNumber).IsUnique(); b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.TotalAmount).HasPrecision(18, 2); b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.NoAction);
        b.Property(x => x.CreatedByUserId).HasMaxLength(128).IsRequired();
        b.Property(x => x.UpdatedByUserId).HasMaxLength(128); b.Property(x => x.CancelledByUserId).HasMaxLength(128);
        b.Property(x => x.ConfirmedByUserId).HasMaxLength(128);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ConfirmedByUserId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CancelledByUserId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.ReceiptDate, x.Id }); b.HasIndex(x => new { x.Status, x.ReceiptDate, x.Id });
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PurchaseReceiptId).OnDelete(DeleteBehavior.NoAction);
    }
}
