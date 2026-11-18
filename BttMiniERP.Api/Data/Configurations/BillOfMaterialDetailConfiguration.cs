using BttMiniERP.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BttMiniERP.Api.Data.Configurations;

public sealed class BillOfMaterialDetailConfiguration : IEntityTypeConfiguration<BillOfMaterialDetail>
{
    public void Configure(EntityTypeBuilder<BillOfMaterialDetail> b)
    {
        b.ToTable("BillOfMaterialDetails", t => t.HasCheckConstraint("CK_BillOfMaterialDetails_QuantityPerUnit", "[QuantityPerUnit] > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.QuantityPerUnit).HasPrecision(18, 3);
        b.HasIndex(x => new { x.BillOfMaterialId, x.RawMaterialId }).IsUnique();
        b.HasOne<Item>().WithMany().HasForeignKey(x => x.RawMaterialId).OnDelete(DeleteBehavior.NoAction);
    }
}
