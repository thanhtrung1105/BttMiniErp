using System.Net;
using System.Net.Http.Json;
using BttMiniERP.Api.Entities;
using BttMiniERP.Tests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace BttMiniERP.Tests.Integration.Auth;

// Store double mô phỏng stamp và bản đọc độc lập; không phải bằng chứng SQL Server.
public sealed class LoginConcurrencyTests
{
    private const string Password = "Regression-Password!7382";

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public async Task ConcurrentWrongPasswordsReloadStateWithoutLosingFailures(int count)
    {
        await using var factory = new SecurityApiFactory();
        var user = await factory.AddUserAsync("parallel-user", Password, "Viewer");
        var store = (MemoryIdentityStore)factory.Services.GetRequiredService<IUserStore<ApplicationUser>>();
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        store.BeforeUpdate = async () =>
        {
            if (Interlocked.Increment(ref arrived) == count) barrier.TrySetResult();
            await barrier.Task.WaitAsync(TimeSpan.FromSeconds(15));
        };
        var callsBefore = store.UpdateCalls;
        var started = DateTimeOffset.UtcNow;
        using var client = factory.CreateClient();
        var responses = await Task.WhenAll(Enumerable.Range(0, count).Select(_ =>
            client.PostAsJsonAsync("/api/v1/auth/login", new { userName = user.UserName, password = "wrong" })));
        foreach (var response in responses)
        {
            using (response) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        Assert.True(store.UpdateCalls - callsBefore > count); // Thật sự đã xảy ra conflict/retry.
        var stored = (await store.FindByIdAsync(user.Id, default))!;
        Assert.Equal(count == 5 ? 0 : count, stored.AccessFailedCount); // Identity reset count khi khóa.
        if (count == 4)
        {
            Assert.Null(stored.LockoutEnd);
        }
        else
        {
            Assert.InRange(stored.LockoutEnd!.Value, started.AddMinutes(15), DateTimeOffset.UtcNow.AddMinutes(15));
            using var locked = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = user.UserName, password = Password });
            Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
            var after = (await store.FindByIdAsync(user.Id, default))!;
            Assert.Equal(stored.LockoutEnd, after.LockoutEnd);
            Assert.Equal(0, after.AccessFailedCount);
        }
    }

    [Theory]
    [InlineData(false, "transient")]
    [InlineData(true, "transient")]
    [InlineData(false, "exhausted")]
    [InlineData(true, "exhausted")]
    [InlineData(false, "other")]
    [InlineData(true, "other")]
    public async Task IdentityUpdateFailuresAreRetriedOrReportedWithoutFalseLoginResult(bool correctPassword, string failure)
    {
        await using var factory = new SecurityApiFactory();
        var user = await factory.AddUserAsync("update-user", Password, "Viewer");
        using (var scope = factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await manager.AccessFailedAsync((await manager.FindByIdAsync(user.Id))!)).Succeeded);
        }
        var store = (MemoryIdentityStore)factory.Services.GetRequiredService<IUserStore<ApplicationUser>>();
        var injected = 0;
        store.UpdateFailure = () =>
        {
            if (failure == "transient" && Interlocked.Increment(ref injected) > 1) return null;
            return IdentityResult.Failed(new IdentityError
            {
                Code = failure == "other" ? "StoreFailure" : "ConcurrencyFailure",
                Description = "sensitive-test-detail"
            });
        };
        var callsBefore = store.UpdateCalls;
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { userName = user.UserName, password = correctPassword ? Password : "wrong" });
        var stored = (await store.FindByIdAsync(user.Id, default))!;
        Assert.DoesNotContain("sensitive-test-detail", await response.Content.ReadAsStringAsync());
        if (failure == "transient")
        {
            Assert.Equal(correctPassword ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(correctPassword ? 0 : 2, stored.AccessFailedCount);
            Assert.Equal(2, store.UpdateCalls - callsBefore);
        }
        else
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.DoesNotContain("accessToken", await response.Content.ReadAsStringAsync());
            Assert.Equal(1, stored.AccessFailedCount);
            Assert.Equal(failure == "exhausted" ? 5 : 1, store.UpdateCalls - callsBefore);
        }
    }

    [Fact]
    public async Task CorrectAndWrongConcurrentLoginsRespectTheWinningStateUpdate()
    {
        await using var factory = new SecurityApiFactory();
        var user = await factory.AddUserAsync("mixed-user", Password, "Viewer");
        using (var setup = factory.Services.CreateScope())
        {
            var manager = setup.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var stored = (await manager.FindByIdAsync(user.Id))!;
            for (var i = 0; i < 4; i++) Assert.True((await manager.AccessFailedAsync(stored)).Succeeded);
        }
        var store = (MemoryIdentityStore)factory.Services.GetRequiredService<IUserStore<ApplicationUser>>();
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        store.BeforeUpdate = async () =>
        {
            if (Interlocked.Increment(ref arrived) == 2) barrier.TrySetResult();
            await barrier.Task.WaitAsync(TimeSpan.FromSeconds(15));
        };
        using var client = factory.CreateClient();
        var correctTask = client.PostAsJsonAsync("/api/v1/auth/login", new { userName = user.UserName, password = Password });
        var wrongTask = client.PostAsJsonAsync("/api/v1/auth/login", new { userName = user.UserName, password = "wrong" });
        await Task.WhenAll(correctTask, wrongTask);
        using var correct = await correctTask;
        using var wrong = await wrongTask;
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        var final = (await store.FindByIdAsync(user.Id, default))!;
        if (correct.StatusCode == HttpStatusCode.OK)
        {
            Assert.Null(final.LockoutEnd);
            Assert.Equal(1, final.AccessFailedCount);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Unauthorized, correct.StatusCode);
            Assert.True(final.LockoutEnd > DateTimeOffset.UtcNow);
            Assert.Equal(0, final.AccessFailedCount);
            Assert.DoesNotContain("accessToken", await correct.Content.ReadAsStringAsync());
        }
    }
}
