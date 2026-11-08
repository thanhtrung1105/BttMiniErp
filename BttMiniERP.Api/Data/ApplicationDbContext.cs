using BttMiniERP.Api.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using BttMiniERP.Api.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Api.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Item> Items => Set<Item>();

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
