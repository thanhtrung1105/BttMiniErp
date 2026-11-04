using BttMiniERP.Api.Common;
using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.DTOs.Users;
using BttMiniERP.Api.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BttMiniERP.Api.Services;

public class UserService(ApplicationDbContext context, UserManager<ApplicationUser> users,
    CurrentUser currentUser, ILogger<UserService> logger)
{
    public virtual async Task<UserPage> ListAsync(UserQuery query, CancellationToken cancellationToken)
    {
        _ = currentUser.Id;
        if (query.Role != null) AppRoles.Validate([query.Role]);
        var source = context.Users.AsNoTracking();
        if (query.Id != null) source = source.Where(user => user.Id == query.Id);
        if (!string.IsNullOrWhiteSpace(query.Search)) source = source.Where(user => user.UserName!.Contains(query.Search));
        if (query.Role != null) source = source.Where(user => context.UserRoles.Any(link => link.UserId == user.Id
            && context.Roles.Any(role => role.Id == link.RoleId && role.Name == query.Role)));
        var total = await source.CountAsync(cancellationToken);
        var ordered = (query.SortBy, query.SortDirection) switch
        {
            ("id", "asc") => source.OrderBy(user => user.Id),
            ("id", "desc") => source.OrderByDescending(user => user.Id),
            ("userName", "desc") => source.OrderByDescending(user => user.UserName).ThenBy(user => user.Id),
            ("userName", "asc") => source.OrderBy(user => user.UserName).ThenBy(user => user.Id),
            _ => throw new ApiException(400, "Sắp xếp không hợp lệ.")
        };
        var page = await ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(user => new { user.Id, user.UserName }).ToListAsync(cancellationToken);
        var ids = page.Select(user => user.Id).ToArray();
        var links = await (from link in context.UserRoles join role in context.Roles on link.RoleId equals role.Id
            where ids.Contains(link.UserId) select new { link.UserId, role.Name }).ToListAsync(cancellationToken);
        return new(page.Select(user => new UserResponse(user.Id, user.UserName!,
            links.Where(link => link.UserId == user.Id).Select(link => link.Name!).Order().ToArray())).ToArray(), query.Page, query.PageSize, total);
    }

    public virtual async Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var actor = currentUser.Id;
        var roles = AppRoles.Validate(request.Roles);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await IdentityAdministrationLock.AcquireAsync(context, cancellationToken);
        await EnsureRolesExistAsync(roles, cancellationToken);
        var user = new ApplicationUser { UserName = request.UserName, LockoutEnabled = true };
        EnsureSuccess(await users.CreateAsync(user, request.Password));
        EnsureSuccess(await users.AddToRolesAsync(user, roles));
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Identity user created. ActorId={ActorId}, TargetId={TargetId}", actor, user.Id);
        return new(user.Id, user.UserName!, roles);
    }

    public virtual async Task<UserResponse> SetRolesAsync(string id, SetRolesRequest request, CancellationToken cancellationToken)
    {
        var actor = currentUser.Id;
        var roles = AppRoles.Validate(request.Roles);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await IdentityAdministrationLock.AcquireAsync(context, cancellationToken);
        var user = await context.Users.SingleOrDefaultAsync(user => user.Id == id, cancellationToken)
            ?? throw new ApiException(404, "Không tìm thấy tài khoản.");
        await EnsureRolesExistAsync(roles, cancellationToken);
        var previous = await users.GetRolesAsync(user);
        var adminCount = await (from link in context.UserRoles join role in context.Roles on link.RoleId equals role.Id
            where role.NormalizedName == "ADMIN" select link.UserId).Distinct().CountAsync(cancellationToken);
        AppRoles.EnsureAdminRemains(previous.Contains(AppRoles.Admin), roles, adminCount);
        EnsureSuccess(await users.RemoveFromRolesAsync(user, previous.Except(roles)));
        EnsureSuccess(await users.AddToRolesAsync(user, roles.Except(previous)));
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Identity roles changed. ActorId={ActorId}, TargetId={TargetId}", actor, user.Id);
        return new(user.Id, user.UserName!, roles);
    }

    private async Task EnsureRolesExistAsync(string[] roles, CancellationToken cancellationToken)
    {
        if (await context.Roles.CountAsync(role => roles.Contains(role.Name!), cancellationToken) != roles.Length)
            throw new ApiException(409, "Chưa có đủ role hệ thống. Cần bootstrap được phê duyệt.");
    }

    public static void EnsureSuccess(IdentityResult result)
    {
        if (result.Succeeded) return;
        if (result.Errors.Any(error => error.Code is "DuplicateUserName" or "DuplicateEmail" or "ConcurrencyFailure"))
            throw new ApiException(409, "Tài khoản bị trùng hoặc đã được thay đổi. Hãy kiểm tra lại.");
        throw new ApiException(400, "Thông tin tài khoản hoặc mật khẩu không đáp ứng chính sách.");
    }
}
