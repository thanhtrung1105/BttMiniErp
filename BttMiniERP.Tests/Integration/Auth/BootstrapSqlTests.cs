using System.Data.Common;
using BttMiniERP.Api.Common;
using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.Entities;
using BttMiniERP.Tests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BttMiniERP.Tests.Integration.Auth;

[Collection("Bootstrap SQL")]
[Trait("Category", "SqlServer")]
[Trait("SqlSuite", "Bootstrap")]
public sealed class BootstrapSqlTests
{
    [BootstrapSqlFact]
    public async Task SecondRunPreservesAllIdentityDataAndDoesNotSaveChanges()
    {
        await using var fixture = await BootstrapSqlFixture.OpenAsync();
        var name = BootstrapSqlFixture.NewName();
        using (var first = fixture.CreateProvider())
            await RunAsync(first, BootstrapSqlFixture.Credentials(name));
        await AssertOneAdminAsync(fixture, name);
        // Dữ liệu ngoài trách nhiệm bootstrap ở cả bảy bảng; snapshot phải bảo toàn toàn bộ.
        using (var setup = fixture.CreateProvider())
        await using (var scope = setup.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = await db.Users.SingleAsync();
            var other = new ApplicationUser { UserName = BootstrapSqlFixture.NewName() };
            Assert.True((await users.CreateAsync(other, BootstrapSqlFixture.Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(other, "Viewer")).Succeeded);
            Assert.True((await users.AddToRoleAsync(admin, "Manager")).Succeeded);
            db.UserClaims.Add(new IdentityUserClaim<string> { UserId = other.Id, ClaimType = "test", ClaimValue = "preserve" });
            db.UserLogins.Add(new IdentityUserLogin<string> { UserId = other.Id, LoginProvider = "test", ProviderKey = "preserve" });
            db.UserTokens.Add(new IdentityUserToken<string> { UserId = other.Id, LoginProvider = "test", Name = "preserve", Value = "synthetic" });
            db.RoleClaims.Add(new IdentityRoleClaim<string> { RoleId = await db.Roles.Where(r => r.Name == "Viewer").Select(r => r.Id).SingleAsync(),
                ClaimType = "test", ClaimValue = "preserve" });
            await db.SaveChangesAsync();
        }
        var before = await fixture.SnapshotAsync();
        var writes = new WriteProbe(rejectWrites: true);
        using (var second = fixture.CreateProvider(writes))
        {
            await RunAsync(second, BootstrapSqlFixture.Credentials(name));
            Assert.True((await fixture.SnapshotAsync()).SequenceEqual(before), "Chạy lại cùng credentials phải giữ nguyên dữ liệu.");
            await RunAsync(second, BootstrapSqlFixture.Credentials(name, "Different-Test-Only!8193"));
            Assert.True((await fixture.SnapshotAsync()).SequenceEqual(before), "Không được ghi đè password của cùng Admin.");
            // Cấu hình khác không được ghi đè Admin hoặc tạo user mới.
            await RunAsync(second, BootstrapSqlFixture.Credentials(BootstrapSqlFixture.NewName(), "Different-Test-Only!8193"));
            // Khi đã có Admin, credentials không còn bắt buộc.
            await RunAsync(second, new ConfigurationBuilder().Build());
        }
        Assert.Equal(0, writes.Attempts);
        Assert.True((await fixture.SnapshotAsync()).SequenceEqual(before), "Dữ liệu Identity thay đổi khi chạy lại bootstrap.");
    }

    [BootstrapSqlFact]
    public async Task PendingMigrationRejectsBeforeAnyIdentitySaveWithoutChangingHistory()
    {
        await using var fixture = await BootstrapSqlFixture.OpenAsync();
        var before = await fixture.SnapshotAsync();
        var writes = new WriteProbe(rejectWrites: true);
        using var provider = fixture.CreateProvider(writes, pendingMigration: true);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(new[] { BootstrapPendingMigrationsAssembly.PendingId }, await db.Database.GetPendingMigrationsAsync());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IdentitySeeder>()
            .RunAsync(BootstrapSqlFixture.Credentials(BootstrapSqlFixture.NewName()), default));
        Assert.Equal("Schema còn migration pending; không được seed.", error.Message);
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal(0, writes.Attempts);
        Assert.True((await fixture.SnapshotAsync()).SequenceEqual(before), "Pending migration phải từ chối trước ghi Identity.");
        Assert.Equal(new[] { IdentityDatabaseVerifier.InitialMigration, IdentityDatabaseVerifier.KeyMigration },
            await db.Database.GetAppliedMigrationsAsync());
    }

    [BootstrapSqlFact]
    public async Task ExistingNonAdminIsNotPromotedAndNewRolesAreRolledBack()
    {
        await using var fixture = await BootstrapSqlFixture.OpenAsync();
        var name = BootstrapSqlFixture.NewName();
        using (var setup = fixture.CreateProvider())
        await using (var scope = setup.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await roles.CreateAsync(new IdentityRole("Viewer"))).Succeeded);
            var user = new ApplicationUser { UserName = name, LockoutEnabled = true };
            Assert.True((await users.CreateAsync(user, BootstrapSqlFixture.Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Viewer")).Succeeded);
        }
        var before = await fixture.SnapshotAsync();
        var writes = new WriteProbe();
        using (var attempt = fixture.CreateProvider(writes))
        {
            var error = await Assert.ThrowsAsync<ApiException>(() => RunAsync(attempt,
                BootstrapSqlFixture.Credentials(name, "Different-Test-Only!8193")));
            Assert.Equal(409, error.StatusCode);
        }
        Assert.Equal(6, writes.Attempts); // Sáu role thiếu đã đi qua SaveChanges trong transaction.
        Assert.True((await fixture.SnapshotAsync()).SequenceEqual(before), "Không được đổi user/password/role hoặc để lại role ghi dở.");
    }

    [BootstrapSqlFact]
    public Task FailureAfterFirstRoleSaveRollsBackEverything() => CheckRollbackAsync(failMembership: false);

    [BootstrapSqlFact]
    public Task FailureAfterMembershipSaveRollsBackEverything() => CheckRollbackAsync(failMembership: true);

    private static async Task CheckRollbackAsync(bool failMembership)
    {
        await using var fixture = await BootstrapSqlFixture.OpenAsync();
        var before = await fixture.SnapshotAsync();
        var fault = new BootstrapFailure(failMembership);
        using (var provider = fixture.CreateProvider(fault))
            await Assert.ThrowsAsync<InjectedBootstrapFailure>(() => RunAsync(provider,
                BootstrapSqlFixture.Credentials(BootstrapSqlFixture.NewName())));
        Assert.True(fault.ObservedPersistedStage, "Test phải gây lỗi sau khi dữ liệu trước đó đã được lưu trong transaction.");
        Assert.True((await fixture.SnapshotAsync()).SequenceEqual(before), "Transaction bootstrap không rollback toàn bộ.");
    }

    [BootstrapSqlFact]
    public async Task ConcurrentBootstrapsWaitOnSqlApplicationLockAndCreateOnlyOneAdmin()
    {
        await using var fixture = await BootstrapSqlFixture.OpenAsync();
        var holder = new HoldAcquiredLock();
        var contender = new ObserveContendedLock();
        using var firstProvider = fixture.CreateProvider(holder);
        using var secondProvider = fixture.CreateProvider(contender);
        var firstName = BootstrapSqlFixture.NewName();
        var first = RunAsync(firstProvider, BootstrapSqlFixture.Credentials(firstName));
        Task? second = null;
        try
        {
            await holder.Acquired.Task.WaitAsync(TimeSpan.FromSeconds(20));
            second = RunAsync(secondProvider, BootstrapSqlFixture.Credentials(BootstrapSqlFixture.NewName()));
            await contender.BeforeAcquire.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.NotSame(holder.Connection, contender.Connection);
            Assert.Equal(0, contender.LockAvailable); // APPLOCK_TEST trên connection thứ hai, không phải lock bộ nhớ.
            await BootstrapLockEvidence.WaitForWaiterAsync(fixture, holder.Connection!, contender.Connection!);
            Assert.False(second.IsCompleted);
        }
        finally
        {
            holder.Release.TrySetResult();
            // Không cleanup khi một context còn ghi, kể cả khi assertion/barrier thất bại.
            await Task.WhenAll(second == null ? new[] { first } : new[] { first, second });
        }
        Assert.True(contender.SawCommittedAdminAfterAcquire);
        Assert.True(contender.ReturnCode >= 0, "Contender phải nhận kết quả acquire thành công từ SQL.");
        await AssertOneAdminAsync(fixture, firstName);
    }

    [BootstrapSqlFact]
    public async Task BootstrapLockTimeoutLeavesNoDataAndRetryAfterReleaseSucceeds()
    {
        await using var fixture = await BootstrapSqlFixture.OpenAsync();
        var before = await fixture.SnapshotAsync();
        using var owner = fixture.CreateProvider();
        await using var ownerScope = owner.CreateAsyncScope();
        var ownerDb = ownerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using (var transaction = await ownerDb.Database.BeginTransactionAsync())
        {
            await IdentityAdministrationLock.AcquireAsync(ownerDb, default);
            var writes = new WriteProbe(rejectWrites: true);
            var evidence = new ObserveContendedLock();
            using var contender = fixture.CreateProvider(writes, additionalInterceptor: evidence);
            var attempt = RunAsync(contender, BootstrapSqlFixture.Credentials(BootstrapSqlFixture.NewName()));
            try
            {
                await evidence.BeforeAcquire.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await BootstrapLockEvidence.WaitForWaiterAsync(fixture, ownerDb.Database.GetDbConnection(), evidence.Connection!);
                var error = await Assert.ThrowsAsync<SqlException>(() => attempt.WaitAsync(TimeSpan.FromSeconds(20)));
                Assert.Equal(51010, error.Number);
                Assert.Equal(-1, evidence.ReturnCode); // Giá trị SQL thật; -2/-3 không được coi là timeout.
            }
            finally
            {
                // Kết thúc contender trước khi provider/fixture cleanup, kể cả assertion thất bại.
                try { await attempt.WaitAsync(TimeSpan.FromSeconds(35)); }
                catch (SqlException) { /* Lỗi được assertion trong try kiểm tra; không nuốt lỗi của assertion. */ }
            }
            Assert.Equal(0, writes.Attempts);
            await transaction.RollbackAsync();
        }
        await BootstrapLockEvidence.AssertNoLocksAsync(ownerDb);
        Assert.True((await fixture.SnapshotAsync()).SequenceEqual(before), "Timeout không được ghi Identity.");
        var name = BootstrapSqlFixture.NewName();
        using var retry = fixture.CreateProvider();
        await RunAsync(retry, BootstrapSqlFixture.Credentials(name));
        await AssertOneAdminAsync(fixture, name);
    }

    private static async Task RunAsync(ServiceProvider provider, IConfiguration config)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().RunAsync(config, deadline.Token); }
        finally
        {
            Assert.Null(db.Database.CurrentTransaction);
            await BootstrapLockEvidence.AssertNoLocksAsync(db);
        }
    }

    private static async Task AssertOneAdminAsync(BootstrapSqlFixture fixture, string name)
    {
        using var provider = fixture.CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(AppRoles.All.Order(), await db.Roles.Select(x => x.Name!).OrderBy(x => x).ToArrayAsync());
        var user = Assert.Single(await db.Users.AsNoTracking().ToArrayAsync());
        Assert.Equal(name, user.UserName);
        Assert.Equal(new[] { "Admin" }, await (from link in db.UserRoles join role in db.Roles on link.RoleId equals role.Id
            where link.UserId == user.Id select role.Name).ToArrayAsync());
        Assert.Equal(1, await db.UserRoles.CountAsync());
    }

    private sealed class WriteProbe(bool rejectWrites = false) : SaveChangesInterceptor
    {
        public int Attempts { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (rejectWrites) throw new InjectedBootstrapFailure();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class InjectedBootstrapFailure : Exception;

    private sealed class BootstrapFailure(bool failMembership) : SaveChangesInterceptor
    {
        public bool ObservedPersistedStage { get; private set; }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            var db = (ApplicationDbContext)eventData.Context!;
            if (!failMembership || await db.UserRoles.AsNoTracking().AnyAsync(cancellationToken))
            {
                Assert.NotNull(db.Database.CurrentTransaction);
                Assert.Equal(failMembership ? 7 : 1, await db.Roles.AsNoTracking().CountAsync(cancellationToken));
                if (failMembership)
                {
                    Assert.Equal(1, await db.Users.AsNoTracking().CountAsync(cancellationToken));
                    Assert.Equal(1, await db.UserRoles.AsNoTracking().CountAsync(cancellationToken));
                }
                ObservedPersistedStage = true;
                throw new InjectedBootstrapFailure();
            }
            return result;
        }
    }

    private static bool IsAdministrationLock(DbCommand command)
        => command.CommandText.Contains("sys.sp_getapplock", StringComparison.Ordinal);

    private sealed class HoldAcquiredLock : DbCommandInterceptor
    {
        public TaskCompletionSource Acquired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DbConnection? Connection { get; private set; }
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (IsAdministrationLock(command))
            {
                Connection = command.Connection;
                Acquired.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }

    private sealed class ObserveContendedLock : DbCommandInterceptor
    {
        public TaskCompletionSource BeforeAcquire { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DbConnection? Connection { get; private set; }
        public int LockAvailable { get; private set; } = -1;
        public int? ReturnCode { get; private set; }
        public bool SawCommittedAdminAfterAcquire { get; private set; }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (IsAdministrationLock(command))
            {
                Connection = command.Connection;
                // Instrumentation chỉ trong test: phát return code trước THROW gốc; không sửa timeout/resource/transaction.
                const string marker = "IF @result < 0";
                Assert.Contains(marker, command.CommandText);
                command.CommandText = command.CommandText.Replace(marker,
                    "RAISERROR(N'BootstrapLockResult:%d', 10, 1, @result) WITH NOWAIT; " + marker, StringComparison.Ordinal);
                ((SqlConnection)command.Connection!).InfoMessage += (_, args) =>
                {
                    foreach (SqlError error in args.Errors)
                        RecordReturnCode(error);
                };
                await using var probe = command.Connection!.CreateCommand();
                probe.Transaction = command.Transaction;
                probe.CommandText = "SELECT APPLOCK_TEST(N'public',N'BttMiniERP.Identity.Administration',N'Exclusive',N'Transaction')";
                LockAvailable = Convert.ToInt32(await probe.ExecuteScalarAsync(cancellationToken));
                BeforeAcquire.TrySetResult();
            }
            return result;
        }
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (IsAdministrationLock(command))
            {
                await using var probe = command.Connection!.CreateCommand();
                probe.Transaction = command.Transaction;
                probe.CommandText = "SELECT COUNT(*) FROM dbo.AspNetUserRoles ur JOIN dbo.AspNetRoles r ON r.Id=ur.RoleId WHERE r.NormalizedName=N'ADMIN'";
                SawCommittedAdminAfterAcquire = Convert.ToInt32(await probe.ExecuteScalarAsync(cancellationToken)) == 1;
            }
            return result;
        }
        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            // SqlClient có thể gộp thông điệp severity 10 vào Errors khi batch kết thúc bằng THROW.
            if (IsAdministrationLock(command) && eventData.Exception is SqlException sql)
                foreach (SqlError error in sql.Errors) RecordReturnCode(error);
            return Task.CompletedTask;
        }

        private void RecordReturnCode(SqlError error)
        {
            const string prefix = "BootstrapLockResult:";
            if (error.Class is 0 or 10 && error.Message.StartsWith(prefix, StringComparison.Ordinal))
                ReturnCode = int.Parse(error.Message[prefix.Length..], System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
