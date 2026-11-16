using BttMiniERP.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace BttMiniERP.Api.Data.Configurations;

public sealed class PurchaseReceiptLineConfiguration : IEntityTypeConfiguration<PurchaseReceiptLine>
{
    public void Configure(EntityTypeBuilder<PurchaseReceiptLine> b)
    {
        b.ToTable("PurchaseReceiptLines", t => {
            t.HasCheckConstraint("CK_PurchaseReceiptLines_Quantity", "[Quantity] > 0");
            t.HasCheckConstraint("CK_PurchaseReceiptLines_UnitPrice", "[UnitPrice] >= 0");
            t.HasCheckConstraint("CK_PurchaseReceiptLines_LineTotal", "[LineTotal] >= 0");
            t.HasCheckConstraint("CK_PurchaseReceiptLines_LineNumber", "[LineNumber] > 0");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.UnitPrice).HasPrecision(18, 2); b.Property(x => x.LineTotal).HasPrecision(18, 2);
        b.HasIndex(x => new { x.PurchaseReceiptId, x.ItemId }).IsUnique();
        b.HasIndex(x => new { x.PurchaseReceiptId, x.LineNumber }).IsUnique();
        b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.NoAction);
    }
}
