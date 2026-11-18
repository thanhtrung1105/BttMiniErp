using BttMiniERP.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace BttMiniERP.Api.Data.Configurations;

public sealed class SalesOrderLineConfiguration : IEntityTypeConfiguration<SalesOrderLine>
{
    public void Configure(EntityTypeBuilder<SalesOrderLine> b)
    {
        b.ToTable("SalesOrderLines", t =>
        {
            t.HasCheckConstraint("CK_SalesOrderLines_Quantity", "[Quantity] > 0");
            t.HasCheckConstraint("CK_SalesOrderLines_UnitPrice", "[UnitPrice] >= 0");
            t.HasCheckConstraint("CK_SalesOrderLines_LineTotal", "[LineTotal] >= 0");
            t.HasCheckConstraint("CK_SalesOrderLines_LineNumber", "[LineNumber] > 0");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.UnitPrice).HasPrecision(18, 2); b.Property(x => x.LineTotal).HasPrecision(18, 2);
        b.HasIndex(x => new { x.SalesOrderId, x.ItemId }).IsUnique();
        b.HasIndex(x => new { x.SalesOrderId, x.LineNumber }).IsUnique();
        b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.NoAction);
    }
}
