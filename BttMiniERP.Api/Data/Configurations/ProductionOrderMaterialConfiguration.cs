using BttMiniERP.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BttMiniERP.Api.Data.Configurations;

public sealed class ProductionOrderMaterialConfiguration : IEntityTypeConfiguration<ProductionOrderMaterial>
{
    public void Configure(EntityTypeBuilder<ProductionOrderMaterial> b)
    {
        b.ToTable("ProductionOrderMaterials", t => {
            t.HasCheckConstraint("CK_ProductionOrderMaterials_PerUnit", "[QuantityPerUnitSnapshot] > 0");
            t.HasCheckConstraint("CK_ProductionOrderMaterials_Required", "[RequiredQuantity] > 0");
            t.HasCheckConstraint("CK_ProductionOrderMaterials_Actual", "[ActualQuantity] IS NULL OR [ActualQuantity] > 0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.QuantityPerUnitSnapshot).HasPrecision(18, 3);
        b.Property(x => x.RequiredQuantity).HasPrecision(18, 3);
        b.Property(x => x.ActualQuantity).HasPrecision(18, 3);
        b.HasIndex(x => new { x.ProductionOrderId, x.RawMaterialId }).IsUnique();
        b.HasOne<Item>().WithMany().HasForeignKey(x => x.RawMaterialId).OnDelete(DeleteBehavior.NoAction);
    }
}
