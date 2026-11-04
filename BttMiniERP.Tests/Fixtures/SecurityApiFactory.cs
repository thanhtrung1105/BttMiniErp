using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.DTOs.Users;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BttMiniERP.Tests.Fixtures;

public sealed class SecurityApiFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IUserStore<ApplicationUser>, MemoryIdentityStore>();
            services.AddScoped<UserService, HttpOnlyUserService>();
        });
    }

    public async Task<ApplicationUser> AddUserAsync(string name, string password, params string[] roles)
    {
        using var scope = Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = name, LockoutEnabled = true };
        Assert.True((await manager.CreateAsync(user, password)).Succeeded);
        Assert.True((await manager.AddToRolesAsync(user, roles)).Succeeded);
        return user;
    }

    // Chỉ thay persistence service cho HTTP contract tests; luật/transaction thật kiểm tra riêng trên SQL.
    private sealed class HttpOnlyUserService(CurrentUser actor) : UserService(null!, null!, null!, NullLogger<UserService>.Instance)
    {
        public override Task<UserPage> ListAsync(UserQuery query, CancellationToken cancellationToken)
            => Task.FromResult(new UserPage([new(actor.Id, "actor", [AppRoles.Admin])], query.Page, query.PageSize, 1));
        public override Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new UserResponse("created-test-user", request.UserName, AppRoles.Validate(request.Roles)));
        public override Task<UserResponse> SetRolesAsync(string id, SetRolesRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new UserResponse(id, "actor", AppRoles.Validate(request.Roles)));
    }
}
