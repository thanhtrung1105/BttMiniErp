using BttMiniERP.Api.Common;
using BttMiniERP.Api.DTOs.Auth;
using BttMiniERP.Api.Entities;
using Microsoft.AspNetCore.Identity;

namespace BttMiniERP.Api.Services;

public sealed class AuthService(IServiceScopeFactory scopes, TokenService tokens, ILogger<AuthService> logger)
{
    private const int MaxAttempts = 5;
    private static readonly ApplicationUser DummyUser = new();
    private static readonly PasswordHasher<ApplicationUser> DummyHasher = new();
    private static readonly string DummyHash = DummyHasher.HashPassword(DummyUser, Guid.NewGuid().ToString());

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Mỗi lần thử dùng UserManager/DbContext mới: không retry trên entity
            // đã bị EF tracking với ConcurrencyStamp và bộ đếm lỗi thời.
            await using var scope = scopes.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByNameAsync(request.UserName);
            if (user == null)
            {
                DummyHasher.VerifyHashedPassword(DummyUser, DummyHash, request.Password);
                throw InvalidLogin();
            }
            if (await users.IsLockedOutAsync(user)) throw InvalidLogin();
            var passwordValid = await users.CheckPasswordAsync(user, request.Password);
            var result = passwordValid
                ? await users.ResetAccessFailedCountAsync(user)
                : await users.AccessFailedAsync(user);
            if (!result.Succeeded)
            {
                if (result.Errors.Any() && result.Errors.All(error => error.Code == "ConcurrencyFailure"))
                    continue;
                logger.LogError(new EventId(1401, "IdentityUpdateFailed"), "Không lưu được trạng thái đăng nhập Identity.");
                throw PersistenceUnavailable();
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!passwordValid) throw InvalidLogin();
            return tokens.Create(user, await users.GetRolesAsync(user));
        }
        logger.LogError(new EventId(1402, "IdentityConcurrencyExhausted"), "Hết số lần thử cập nhật trạng thái đăng nhập Identity.");
        throw PersistenceUnavailable();
    }

    private static ApiException InvalidLogin() => new(401, "Thông tin đăng nhập không hợp lệ.");
    private static ApiException PersistenceUnavailable() => new(503, "Chưa thể xử lý đăng nhập. Vui lòng thử lại sau.");
}
