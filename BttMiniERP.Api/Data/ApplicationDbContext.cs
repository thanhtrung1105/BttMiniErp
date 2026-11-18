using BttMiniERP.Api.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using BttMiniERP.Api.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Api.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();
    public DbSet<ProductionOrderMaterial> ProductionOrderMaterials => Set<ProductionOrderMaterial>();
    public DbSet<BillOfMaterial> BillOfMaterials => Set<BillOfMaterial>();
    public DbSet<BillOfMaterialDetail> BillOfMaterialDetails => Set<BillOfMaterialDetail>();
    public DbSet<PurchaseReceipt> PurchaseReceipts => Set<PurchaseReceipt>();
    public DbSet<PurchaseReceiptLine> PurchaseReceiptLines => Set<PurchaseReceiptLine>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<InventoryBalance> InventoryBalances => Set<InventoryBalance>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateInventoryWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateInventoryWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ValidateInventoryWrites()
    {
        if (ChangeTracker.Entries<InventoryTransaction>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Không sửa hoặc xóa lịch sử kho.");
        if (ChangeTracker.Entries<InventoryBalance>().Any(x => x.State is EntityState.Modified or EntityState.Deleted
            || (x.State == EntityState.Added && x.Entity.QuantityOnHand != 0)))
            throw new InvalidOperationException("Chỉ InventoryService được thay đổi số dư kho; balance mới phải bằng 0.");
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        IdentityPersistenceConfiguration.ConfigureModel(builder);

        // Không xóa dây chuyền dữ liệu Identity; việc xóa phải được xử lý tường minh.
        foreach (var foreignKey in builder.Model.GetEntityTypes().SelectMany(entity => entity.GetForeignKeys()))
        {
            foreignKey.DeleteBehavior = DeleteBehavior.NoAction;
        }
    }
}
