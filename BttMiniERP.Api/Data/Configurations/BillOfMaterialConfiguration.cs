using BttMiniERP.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BttMiniERP.Api.Data.Configurations;

public sealed class BillOfMaterialConfiguration : IEntityTypeConfiguration<BillOfMaterial>
{
    public void Configure(EntityTypeBuilder<BillOfMaterial> b)
    {
        b.ToTable("BillOfMaterials", t => t.HasCheckConstraint("CK_BillOfMaterials_Version", "[Version] >= 1"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.HasIndex(x => x.FinishedProductId).IsUnique();
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Property(x => x.CreatedByUserId).HasMaxLength(128).IsRequired();
        b.Property(x => x.UpdatedByUserId).HasMaxLength(128);
        b.HasOne<Item>().WithMany().HasForeignKey(x => x.FinishedProductId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(x => x.Details).WithOne().HasForeignKey(x => x.BillOfMaterialId).OnDelete(DeleteBehavior.NoAction);
    }
}
