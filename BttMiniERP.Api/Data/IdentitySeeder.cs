using BttMiniERP.Api.Common;
using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Api.Data;

public sealed class IdentitySeeder(ApplicationDbContext context, UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles)
{
    public static void ValidateTarget(IConfiguration configuration, string environment, string[] args)
    {
        var connection = new SqlConnectionStringBuilder(DatabaseConnectionSettings.Validate(configuration.GetConnectionString("DefaultConnection"), environment));
        if (!args.Contains("--seed-identity=true", StringComparer.Ordinal)
            || !args.Contains("--approved-server=" + connection.DataSource, StringComparer.Ordinal)
            || !args.Contains("--approved-database=" + connection.InitialCatalog, StringComparer.Ordinal))
            throw new InvalidOperationException("Seed cần chỉ định rõ server/database đã được duyệt trên dòng lệnh.");
    }

    public async Task RunAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        // Chỉ được gọi từ chế độ CLI riêng; không migrate hoặc seed trong startup HTTP.
        await context.Database.OpenConnectionAsync(cancellationToken);
        if ((await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            throw new InvalidOperationException("Schema còn migration pending; không được seed.");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await IdentityAdministrationLock.AcquireAsync(context, cancellationToken);
        foreach (var role in AppRoles.All)
            if (!await roles.RoleExistsAsync(role)) UserService.EnsureSuccess(await roles.CreateAsync(new IdentityRole(role)));
        var hasAdmin = await (from link in context.UserRoles join role in context.Roles on link.RoleId equals role.Id
            where role.NormalizedName == "ADMIN" select link).AnyAsync(cancellationToken);
        if (!hasAdmin)
        {
            var name = configuration["BootstrapAdmin:UserName"];
            var password = configuration["BootstrapAdmin:Password"];
            if (string.IsNullOrWhiteSpace(name) || name.Length > 256 || string.IsNullOrEmpty(password) || password.Length > 128)
                throw new InvalidOperationException("Thiếu hoặc sai cấu hình bootstrap admin từ secret.");
            if (await users.FindByNameAsync(name) != null)
                throw new ApiException(409, "Tài khoản bootstrap đã tồn tại nhưng không phải Admin; cần review riêng.");
            var admin = new ApplicationUser { UserName = name, LockoutEnabled = true };
            UserService.EnsureSuccess(await users.CreateAsync(admin, password));
            UserService.EnsureSuccess(await users.AddToRoleAsync(admin, AppRoles.Admin));
        }
        await transaction.CommitAsync(cancellationToken);
    }
}
