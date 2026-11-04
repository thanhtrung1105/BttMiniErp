using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Api.Data;

using BttMiniERP.Api.Data.Configurations;
using Microsoft.AspNetCore.Identity;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(
        this IServiceCollection services, IConfiguration configuration, string environment)
    {
        services.Configure<IdentityOptions>(IdentityPersistenceConfiguration.ConfigureOptions);
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            // Chỉ đọc cấu hình khi resolve context. Health/OpenAPI không cần SQL hoặc secrets.
            var connection = DatabaseConnectionSettings.Validate(
                configuration.GetConnectionString("DefaultConnection"), environment);
            options.UseSqlServer(connection);
        });
        return services;
    }
}
