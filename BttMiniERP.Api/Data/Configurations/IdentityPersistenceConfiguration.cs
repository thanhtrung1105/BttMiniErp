    using BttMiniERP.Api.Entities;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.EntityFrameworkCore;

    namespace BttMiniERP.Api.Data.Configurations;

    public static class IdentityPersistenceConfiguration
    {
        public const int MaxKeyLength = 128;

        public static void ConfigureOptions(IdentityOptions options)
        {
            options.Stores.MaxLengthForKeys = MaxKeyLength;
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version1;
        }

        public static void ConfigureModel(ModelBuilder builder)
        {
            // Mapping chung cho runtime/factory/test; MaxLengthForKeys không giới hạn Id/UserId/RoleId.
            builder.Entity<ApplicationUser>().Property(x => x.Id).HasMaxLength(MaxKeyLength);
            builder.Entity<IdentityRole>().Property(x => x.Id).HasMaxLength(MaxKeyLength);
            builder.Entity<IdentityUserClaim<string>>().Property(x => x.UserId).HasMaxLength(MaxKeyLength);
            builder.Entity<IdentityRoleClaim<string>>().Property(x => x.RoleId).HasMaxLength(MaxKeyLength);
            builder.Entity<IdentityUserRole<string>>(entity =>
            {
                entity.Property(x => x.UserId).HasMaxLength(MaxKeyLength);
                entity.Property(x => x.RoleId).HasMaxLength(MaxKeyLength);
            });
            builder.Entity<IdentityUserLogin<string>>(entity =>
            {
                entity.Property(x => x.UserId).HasMaxLength(MaxKeyLength);
                entity.Property(x => x.LoginProvider).HasMaxLength(MaxKeyLength);
                entity.Property(x => x.ProviderKey).HasMaxLength(MaxKeyLength);
            });
            builder.Entity<IdentityUserToken<string>>(entity =>
            {
                entity.Property(x => x.UserId).HasMaxLength(MaxKeyLength);
                entity.Property(x => x.LoginProvider).HasMaxLength(MaxKeyLength);
                entity.Property(x => x.Name).HasMaxLength(MaxKeyLength);
            });
        }
    }
