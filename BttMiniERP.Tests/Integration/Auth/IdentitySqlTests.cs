using BttMiniERP.Api.Common;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.DTOs.Auth;
using BttMiniERP.Api.DTOs.Users;
using BttMiniERP.Api.Services;
using BttMiniERP.Tests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
using System.Collections.Concurrent;
using BttMiniERP.Api.Entities;
using Microsoft.Data.SqlClient;

namespace BttMiniERP.Tests.Integration.Auth;

[Collection("Auth SQL")]
[Trait("Category", "SqlServer")]
[Trait("SqlSuite", "Auth")]
public sealed class IdentitySqlTests(AuthSqlFixture fixture)
{
    [AuthSqlFact]
    public async Task IdentitySqlLoginPersistsLockoutAndRejectsCorrectPasswordWhileLocked()
    {
        try
        {
            using var provider = fixture.CreateProvider();
            using var scope = provider.CreateScope();
            var user = await fixture.CreateUserAsync(scope.ServiceProvider, "Viewer");
            var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
            var success = await auth.LoginAsync(new LoginRequest { UserName = user.UserName!, Password = AuthSqlFixture.Password }, default);
            Assert.Contains("Viewer", success.Roles);
            for (var attempt = 0; attempt < 5; attempt++)
                Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => auth.LoginAsync(new LoginRequest { UserName = user.UserName!, Password = "wrong" }, default))).StatusCode);
            Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => auth.LoginAsync(new LoginRequest { UserName = user.UserName!, Password = AuthSqlFixture.Password }, default))).StatusCode);
            using var reader = provider.CreateScope();
            var persisted = await reader.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
            Assert.True(persisted.LockoutEnd > DateTimeOffset.UtcNow);
            Assert.NotEqual(AuthSqlFixture.Password, persisted.PasswordHash);
        }
        finally { await fixture.CleanupUsersAsync(); }
    }

    [AuthSqlFact]
    public async Task ManagementCreatesListsChangesRolesAndRejectsDuplicateAndLastAdmin()
    {
        try
        {
            using var provider = fixture.CreateProvider();
            using var scope = provider.CreateScope();
            var admin = await fixture.CreateUserAsync(scope.ServiceProvider, "Admin");
            AuthSqlFixture.SetActor(scope.ServiceProvider, admin.Id);
            var service = scope.ServiceProvider.GetRequiredService<UserService>();
            var name = "p14_" + Guid.NewGuid().ToString("N");
            var user = await service.CreateAsync(new CreateUserRequest { UserName = name, Password = AuthSqlFixture.Password, Roles = ["Viewer"] }, default);
            fixture.Track(user.Id);
            var page = await service.ListAsync(new UserQuery { Id = user.Id, Role = "Viewer" }, default);
            Assert.Single(page.Items);
            Assert.Equal(new[] { "Manager" }, (await service.SetRolesAsync(user.Id, new SetRolesRequest { Roles = ["Manager"] }, default)).Roles);
            Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => service.CreateAsync(new CreateUserRequest { UserName = name, Password = AuthSqlFixture.Password, Roles = [] }, default))).StatusCode);
            // Sau lỗi transaction dùng scope mới để không mang entity tracker đã rollback sang thao tác sau.
            using var fresh = provider.CreateScope();
            AuthSqlFixture.SetActor(fresh.ServiceProvider, admin.Id);
            Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => fresh.ServiceProvider.GetRequiredService<UserService>()
                .SetRolesAsync(admin.Id, new SetRolesRequest { Roles = ["Viewer"] }, default))).StatusCode);
        }
        finally { await fixture.CleanupUsersAsync(); }
    }

    [AuthSqlFact]
    public async Task CreatingUserRollsBackWhenRoleAssignmentFails()
    {
        try
        {
            using var initial = fixture.CreateProvider();
            using var setup = initial.CreateScope();
            var admin = await fixture.CreateUserAsync(setup.ServiceProvider, "Admin");
            var name = "p14_" + Guid.NewGuid().ToString("N");
            using (var failing = fixture.CreateProvider(new FailRoleAssignment()))
            using (var scope = failing.CreateScope())
            {
                AuthSqlFixture.SetActor(scope.ServiceProvider, admin.Id);
                await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<UserService>()
                    .CreateAsync(new CreateUserRequest { UserName = name, Password = AuthSqlFixture.Password, Roles = ["Viewer"] }, default));
            }
            using var read = initial.CreateScope();
            Assert.False(await read.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AnyAsync(user => user.UserName == name));
        }
        finally { await fixture.CleanupUsersAsync(); }
    }

    [AuthSqlFact]
    public async Task ConcurrentDemotionsUsingSeparateConnectionsCannotRemoveBothAdmins()
    {
        try
        {
            using var provider = fixture.CreateProvider();
            string firstId, secondId;
            using (var setup = provider.CreateScope())
            {
                firstId = (await fixture.CreateUserAsync(setup.ServiceProvider, "Admin")).Id;
                secondId = (await fixture.CreateUserAsync(setup.ServiceProvider, "Admin")).Id;
            }
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var arrived = 0;
            async Task<int> Demote(string id)
            {
                using var scope = provider.CreateScope();
                AuthSqlFixture.SetActor(scope.ServiceProvider, id);
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await context.Database.OpenConnectionAsync();
                if (Interlocked.Increment(ref arrived) == 2) ready.SetResult();
                await release.Task;
                try
                {
                    await scope.ServiceProvider.GetRequiredService<UserService>().SetRolesAsync(id, new SetRolesRequest { Roles = ["Viewer"] }, default);
                    return 200;
                }
                catch (ApiException exception) { return exception.StatusCode; }
            }
            var first = Demote(firstId);
            var second = Demote(secondId);
            try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
            finally
            {
                release.TrySetResult();
                // Đợi cả hai connection kết thúc kể cả khi barrier timeout,
                // tránh cleanup chạy đồng thời với thao tác vẫn đang ghi.
                await Task.WhenAll(first, second);
            }
            var outcomes = await Task.WhenAll(first, second);
            Assert.Equal(new[] { 200, 409 }, outcomes.Order());
            using var reader = provider.CreateScope();
            var db = reader.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await (from link in db.UserRoles join role in db.Roles on link.RoleId equals role.Id where role.Name == "Admin" select link).CountAsync());
            Assert.Equal(2, await db.UserRoles.CountAsync(link => link.UserId == firstId || link.UserId == secondId));
        }
        finally { await fixture.CleanupUsersAsync(); }
    }

    private sealed class FailRoleAssignment : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<IdentityUserRole<string>>().Any(entry => entry.State == EntityState.Added))
                throw new InvalidOperationException("Test-only role persistence failure");
            return ValueTask.FromResult(result);
        }
    }

    [AuthSqlFact]
    public async Task ConcurrentWrongLoginsPersistEveryFailureAndLockOnFifthAttempt()
    {
        try
        {
            using var setupProvider = fixture.CreateProvider();
            string userId, userName;
            using (var setup = setupProvider.CreateScope())
            {
                var user = await fixture.CreateUserAsync(setup.ServiceProvider, "Viewer");
                userId = user.Id;
                userName = user.UserName!;
            }
            var barrier = new LoginReadBarrier(userName, 4);
            async Task<int> WrongLogin()
            {
                // Provider/context/connection độc lập, không chia sẻ khóa trong process.
                using var provider = fixture.CreateProvider(barrier);
                using var scope = provider.CreateScope();
                var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
                return (await Assert.ThrowsAsync<ApiException>(() => auth.LoginAsync(
                    new LoginRequest { UserName = userName, Password = "wrong" }, default))).StatusCode;
            }
            var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => WrongLogin()));
            Assert.All(outcomes, outcome => Assert.Equal(401, outcome));
            Assert.True(barrier.Contexts.Count > 4); // Có context mới cho retry sau conflict thật.
            using (var reader = setupProvider.CreateScope())
            {
                var stored = await reader.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == userId);
                Assert.Equal(4, stored.AccessFailedCount);
                Assert.Null(stored.LockoutEnd);
            }
            var started = DateTimeOffset.UtcNow;
            Assert.Equal(401, await WrongLogin());
            DateTimeOffset lockedUntil;
            using (var reader = setupProvider.CreateScope())
            {
                var stored = await reader.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == userId);
                Assert.Equal(0, stored.AccessFailedCount);
                lockedUntil = stored.LockoutEnd!.Value;
                Assert.InRange(lockedUntil, started.AddMinutes(15), DateTimeOffset.UtcNow.AddMinutes(15));
            }
            using (var login = setupProvider.CreateScope())
            {
                Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => login.ServiceProvider.GetRequiredService<AuthService>()
                    .LoginAsync(new LoginRequest { UserName = userName, Password = AuthSqlFixture.Password }, default))).StatusCode);
            }
            using var final = setupProvider.CreateScope();
            var unchanged = await final.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == userId);
            Assert.Equal(lockedUntil, unchanged.LockoutEnd);
            Assert.Equal(0, unchanged.AccessFailedCount);
        }
        finally { await fixture.CleanupUsersAsync(); }
    }

    private sealed class LoginReadBarrier(string userName, int participants) : DbCommandInterceptor
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int reads;
        private int bufferedReads;
        public ConcurrentDictionary<Guid, byte> Contexts { get; } = new();

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM [AspNetUsers]", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value is string value
                    && string.Equals(value, userName, StringComparison.OrdinalIgnoreCase)))
            {
                Contexts.TryAdd(eventData.Context!.ContextId.InstanceId, 0);
                // Đọc hết result trước barrier để SQL thực sự lấy cùng snapshot ban đầu,
                // không chỉ đồng bộ trước lúc SELECT bắt đầu thực thi.
                if (Interlocked.Increment(ref reads) <= participants)
                {
                    var table = new System.Data.DataTable();
                    table.Load(result);
                    await result.DisposeAsync();
                    if (Interlocked.Increment(ref bufferedReads) == participants) ready.TrySetResult();
                    await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
                    return table.CreateDataReader();
                }
            }
            return result;
        }
    }

    [AuthSqlFact]
    public async Task ChangingRolesRollsBackPersistedRemovalWhenAssignmentFails()
    {
        try
        {
            using var provider = fixture.CreateProvider();
            string actorId, targetId;
            string? originalStamp;
            using (var setup = provider.CreateScope())
            {
                actorId = (await fixture.CreateUserAsync(setup.ServiceProvider, "Admin")).Id;
                var target = await fixture.CreateUserAsync(setup.ServiceProvider, "Viewer");
                targetId = target.Id;
                originalStamp = target.ConcurrencyStamp;
            }
            var failure = new FailAfterRoleRemoval(targetId);
            using (var failing = fixture.CreateProvider(failure))
            using (var scope = failing.CreateScope())
            {
                AuthSqlFixture.SetActor(scope.ServiceProvider, actorId);
                await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<UserService>()
                    .SetRolesAsync(targetId, new SetRolesRequest { Roles = ["Manager"] }, default));
                Assert.True(failure.ObservedPersistedRemoval);
            }
            using var reader = provider.CreateScope();
            var db = reader.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(new[] { "Viewer" }, await (from link in db.UserRoles join role in db.Roles on link.RoleId equals role.Id
                where link.UserId == targetId select role.Name).ToArrayAsync());
            Assert.Equal(originalStamp, (await db.Users.AsNoTracking().SingleAsync(user => user.Id == targetId)).ConcurrencyStamp);
            await AssertAdministrationLockAvailableAsync(db);
        }
        finally { await fixture.CleanupUsersAsync(); }
    }

    private sealed class FailAfterRoleRemoval(string userId) : SaveChangesInterceptor
    {
        public bool ObservedPersistedRemoval { get; private set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var db = (ApplicationDbContext)eventData.Context!;
            if (db.ChangeTracker.Entries<IdentityUserRole<string>>().Any(entry => entry.State == EntityState.Added && entry.Entity.UserId == userId))
            {
                Assert.False(await db.UserRoles.AsNoTracking().AnyAsync(link => link.UserId == userId, cancellationToken));
                ObservedPersistedRemoval = true;
                throw new InvalidOperationException("Test-only failure after persisted role removal");
            }
            return result;
        }
    }

    [AuthSqlFact]
    public async Task ExhaustedLoginRetriesReturn503WithoutPersistingFailedCountOrToken()
    {
        try
        {
            using var provider = fixture.CreateProvider();
            string id, name;
            using (var setup = provider.CreateScope())
            {
                var user = await fixture.CreateUserAsync(setup.ServiceProvider, "Viewer");
                id = user.Id;
                name = user.UserName!;
            }
            var conflict = new AdvanceStampBeforeSave(fixture, id);
            using (var competing = fixture.CreateProvider(conflict))
            using (var login = competing.CreateScope())
            {
                var error = await Assert.ThrowsAsync<ApiException>(() => login.ServiceProvider.GetRequiredService<AuthService>()
                    .LoginAsync(new LoginRequest { UserName = name, Password = "wrong" }, default));
                Assert.Equal(503, error.StatusCode);
                Assert.Equal(5, conflict.Conflicts);
            }
            using var reader = provider.CreateScope();
            var stored = await reader.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AsNoTracking().SingleAsync(user => user.Id == id);
            Assert.Equal(0, stored.AccessFailedCount);
            Assert.Null(stored.LockoutEnd);
        }
        finally { await fixture.CleanupUsersAsync(); }
    }

    private sealed class AdvanceStampBeforeSave(AuthSqlFixture fixture, string userId) : SaveChangesInterceptor
    {
        public int Conflicts { get; private set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ApplicationUser>().Any(entry => entry.State == EntityState.Modified && entry.Entity.Id == userId))
            {
                // Connection riêng ghi stamp mới của đúng user test: EF UPDATE phía login
                // phải ảnh hưởng 0 dòng và Identity trả ConcurrencyFailure thật.
                using var provider = fixture.CreateProvider();
                using var scope = provider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var stamp = Guid.NewGuid().ToString();
                Assert.Equal(1, await db.Users.Where(user => user.Id == userId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.ConcurrencyStamp, stamp), cancellationToken));
                Conflicts++;
            }
            return result;
        }
    }

    [AuthSqlFact]
    public async Task AdministrationLockTimesOutAndIsReleasedAfterCommitAndRollback()
    {
        using var provider = fixture.CreateProvider();
        using var owner = provider.CreateScope();
        using var contender = provider.CreateScope();
        var first = owner.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var second = contender.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await first.Database.OpenConnectionAsync();
        await second.Database.OpenConnectionAsync();
        Assert.NotSame(first.Database.GetDbConnection(), second.Database.GetDbConnection());
        await using (var held = await first.Database.BeginTransactionAsync())
        {
            await IdentityAdministrationLock.AcquireAsync(first, default);
            await using (var waiting = await second.Database.BeginTransactionAsync())
            {
                // Helper production có timeout 10 giây và THROW 51010 khi chưa lấy được lock.
                var error = await Assert.ThrowsAsync<SqlException>(() => IdentityAdministrationLock.AcquireAsync(second, default));
                Assert.Equal(51010, error.Number);
                await waiting.RollbackAsync();
            }
            await held.CommitAsync();
        }
        await AssertAdministrationLockAvailableAsync(second);
        await using (var held = await first.Database.BeginTransactionAsync())
        {
            await IdentityAdministrationLock.AcquireAsync(first, default);
            await held.RollbackAsync();
        }
        await AssertAdministrationLockAvailableAsync(second);
    }

    private static async Task AssertAdministrationLockAvailableAsync(ApplicationDbContext context)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        // Nếu transaction trước còn giữ lock thì thất bại ngay, không chờ timeout dài.
        await context.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result=sys.sp_getapplock @Resource=N'BttMiniERP.Identity.Administration',
                @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=0;
            IF @result < 0 THROW 51011, 'Test detected unreleased administration lock.', 1;
            """);
        await transaction.RollbackAsync();
    }
}
