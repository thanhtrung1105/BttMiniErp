using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.Data.Configurations;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace BttMiniERP.Api.Common.Authorization;

public static class SecurityRegistration
{
    public static IServiceCollection AddApplicationSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            IdentityPersistenceConfiguration.ConfigureOptions(options);
            options.Password.RequiredLength = 12;
            options.Password.RequireDigit = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddOptions<JwtSettings>().Bind(configuration.GetSection("Jwt"))
            .Validate(settings => settings.IsValid(), "Cấu hình JWT thiếu hoặc không hợp lệ. Cần issuer, audience, key Base64 >=32 byte và lifetime 5–30 phút.")
            .ValidateOnStart();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtSettings>>((options, settings) =>
            {
                options.MapInboundClaims = false;
                options.SaveToken = false;
                options.IncludeErrorDetails = false;
                options.TokenValidationParameters = settings.Value.ValidationParameters();
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (string.IsNullOrWhiteSpace(context.Principal?.FindFirst("sub")?.Value)) context.Fail("Invalid subject.");
                        return Task.CompletedTask;
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.Headers.WWWAuthenticate = "Bearer";
                        await Results.Problem(statusCode: 401, title: "Yêu cầu token hợp lệ.").ExecuteAsync(context.HttpContext);
                    },
                    OnForbidden = context => Results.Problem(statusCode: 403, title: "Không đủ quyền truy cập.").ExecuteAsync(context.HttpContext)
                };
            });
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            options.AddPolicy(AppRoles.ManageUsers, policy => policy.RequireAuthenticatedUser().RequireRole(AppRoles.Admin));
            options.AddPolicy(AppRoles.ReadMasterData, policy => policy.RequireAuthenticatedUser().RequireRole(AppRoles.All));
            options.AddPolicy(AppRoles.ManageMasterData, policy => policy.RequireAuthenticatedUser().RequireRole(AppRoles.Admin, "Manager"));
        });
        services.AddRateLimiter(options =>
        {
            options.AddPolicy("Login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown-client", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
            options.OnRejected = async (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await Results.Problem(statusCode: 429, title: "Quá nhiều yêu cầu đăng nhập. Vui lòng thử lại sau.").ExecuteAsync(context.HttpContext);
            };
        });
        services.AddHttpContextAccessor();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<CurrentUser>();
        services.AddScoped<TokenService>();
        services.AddScoped<AuthService>();
        services.AddScoped<UserService>();
        services.AddScoped<IdentitySeeder>();
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
        return services;
    }
}
