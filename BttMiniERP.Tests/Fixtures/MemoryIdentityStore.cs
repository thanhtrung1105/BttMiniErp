using System.Collections.Concurrent;
using BttMiniERP.Api.Entities;
using Microsoft.AspNetCore.Identity;

namespace BttMiniERP.Tests.Fixtures;

// Test double của Identity store cho HTTP/lockout; không chứng minh persistence/transaction SQL.
public sealed class MemoryIdentityStore : IUserPasswordStore<ApplicationUser>, IUserLockoutStore<ApplicationUser>, IUserRoleStore<ApplicationUser>
{
    private readonly ConcurrentDictionary<string, ApplicationUser> users = new();
    private readonly ConcurrentDictionary<string, List<string>> roles = new();
    private readonly object sync = new();
    private int updateCalls;
    public int UpdateCalls => updateCalls;
    public Func<Task>? BeforeUpdate { get; set; }
    public Func<IdentityResult?>? UpdateFailure { get; set; }
    public void Dispose() { }
    public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id);
    public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
    public Task SetUserNameAsync(ApplicationUser user, string? name, CancellationToken cancellationToken) { user.UserName = name; return Task.CompletedTask; }
    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedUserName);
    public Task SetNormalizedUserNameAsync(ApplicationUser user, string? name, CancellationToken cancellationToken) { user.NormalizedUserName = name; return Task.CompletedTask; }
    public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken) { users[user.Id] = Copy(user)!; return Task.FromResult(IdentityResult.Success); }
    public async Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref updateCalls);
        if (BeforeUpdate != null) await BeforeUpdate();
        if (UpdateFailure?.Invoke() is { } failure) return failure;
        // Chỉ mô phỏng optimistic concurrency trong test, không thay bằng chứng SQL.
        lock (sync)
        {
            if (!users.TryGetValue(user.Id, out var stored) || stored.ConcurrencyStamp != user.ConcurrencyStamp)
                return IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());
            user.ConcurrencyStamp = Guid.NewGuid().ToString();
            users[user.Id] = Copy(user)!;
            return IdentityResult.Success;
        }
    }
    public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<ApplicationUser?> FindByIdAsync(string id, CancellationToken cancellationToken) => Task.FromResult(Copy(users.GetValueOrDefault(id)));
    public Task<ApplicationUser?> FindByNameAsync(string name, CancellationToken cancellationToken) => Task.FromResult(Copy(users.Values.SingleOrDefault(user => user.NormalizedUserName == name)));
    public Task SetPasswordHashAsync(ApplicationUser user, string? hash, CancellationToken cancellationToken) { user.PasswordHash = hash; return Task.CompletedTask; }
    public Task<string?> GetPasswordHashAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash);
    public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash != null);
    public Task<DateTimeOffset?> GetLockoutEndDateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.LockoutEnd);
    public Task SetLockoutEndDateAsync(ApplicationUser user, DateTimeOffset? end, CancellationToken cancellationToken) { user.LockoutEnd = end; return Task.CompletedTask; }
    public Task<int> IncrementAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(++user.AccessFailedCount);
    public Task ResetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken) { user.AccessFailedCount = 0; return Task.CompletedTask; }
    public Task<int> GetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.AccessFailedCount);
    public Task<bool> GetLockoutEnabledAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.LockoutEnabled);
    public Task SetLockoutEnabledAsync(ApplicationUser user, bool enabled, CancellationToken cancellationToken) { user.LockoutEnabled = enabled; return Task.CompletedTask; }
    public Task AddToRoleAsync(ApplicationUser user, string role, CancellationToken cancellationToken) { roles.GetOrAdd(user.Id, _ => []).Add(role); return Task.CompletedTask; }
    public Task RemoveFromRoleAsync(ApplicationUser user, string role, CancellationToken cancellationToken) { roles.GetOrAdd(user.Id, _ => []).Remove(role); return Task.CompletedTask; }
    public Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult<IList<string>>(roles.GetOrAdd(user.Id, _ => []).Select(role => BttMiniERP.Api.Common.Authorization.AppRoles.All.Single(r => r.ToUpperInvariant() == role)).ToList());
    public Task<bool> IsInRoleAsync(ApplicationUser user, string role, CancellationToken cancellationToken) => Task.FromResult(roles.GetOrAdd(user.Id, _ => []).Contains(role));
    public Task<IList<ApplicationUser>> GetUsersInRoleAsync(string role, CancellationToken cancellationToken)
        => Task.FromResult<IList<ApplicationUser>>(users.Values.Where(user => roles.GetOrAdd(user.Id, _ => []).Contains(role)).ToList());

    private static ApplicationUser? Copy(ApplicationUser? user) => user == null ? null : new()
    {
        Id = user.Id, UserName = user.UserName, NormalizedUserName = user.NormalizedUserName,
        PasswordHash = user.PasswordHash, SecurityStamp = user.SecurityStamp, ConcurrencyStamp = user.ConcurrencyStamp,
        AccessFailedCount = user.AccessFailedCount, LockoutEnd = user.LockoutEnd, LockoutEnabled = user.LockoutEnabled
    };
}
