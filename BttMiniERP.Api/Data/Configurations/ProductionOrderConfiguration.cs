using BttMiniERP.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BttMiniERP.Api.Data.Configurations;

public sealed class ProductionOrderConfiguration : IEntityTypeConfiguration<ProductionOrder>
{
    public void Configure(EntityTypeBuilder<ProductionOrder> b)
    {
        b.ToTable("ProductionOrders", t => {
            t.HasCheckConstraint("CK_ProductionOrders_Status", "[Status] IN (0,1,2)");
            t.HasCheckConstraint("CK_ProductionOrders_Quantity", "[Quantity] > 0");
            t.HasCheckConstraint("CK_ProductionOrders_BomVersion", "[BomVersionSnapshot] >= 1");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.OrderNumber).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.OrderNumber).IsUnique();
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<BillOfMaterial>().WithMany().HasForeignKey(x => x.BomId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Item>().WithMany().HasForeignKey(x => x.FinishedProductId).OnDelete(DeleteBehavior.NoAction);
        b.Property(x => x.CreatedByUserId).HasMaxLength(128).IsRequired();
        b.Property(x => x.UpdatedByUserId).HasMaxLength(128);
        b.Property(x => x.CancelledByUserId).HasMaxLength(128);
        b.Property(x => x.ConfirmedByUserId).HasMaxLength(128);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CancelledByUserId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ConfirmedByUserId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.CreatedAt, x.Id });
        b.HasIndex(x => new { x.Status, x.CreatedAt, x.Id });
        b.HasMany(x => x.Materials).WithOne().HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.NoAction);
    }
}
