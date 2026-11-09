using BttMiniERP.Api.Common;
using BttMiniERP.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
namespace BttMiniERP.Tests.Integration.Partners;
[Collection("Partners SQL"), Trait("Category", "SqlServer"), Trait("SqlSuite", "Partners")]
public sealed partial class PartnersSqlTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [PartnersSqlFact]
    public async Task SupplierCrudNormalizesFiltersPaginatesAndPreservesAudit()
    {
        await using var fixture = await PartnersSqlFixture.OpenAsync();
        await using var db = fixture.Context();
        var service = fixture.Suppliers(db);
        var first = await service.CreateAsync(new() { Code = " cat-a ", Name = " First ", Phone = " 0123456789 ", Email = "a@example.com", Address = " Address " }, Ct);
        var second = await service.CreateAsync(new() { Code = "cat-b", Name = "Second" }, Ct);
        Assert.Equal("CAT-A", first.Code);
        Assert.Equal("First", first.Name);
        Assert.Equal("0123456789", first.Phone);
        Assert.Equal("a@example.com", first.Email);
        Assert.Equal("Address", first.Address);
        Assert.Equal(fixture.ActorId, first.CreatedByUserId);
        Assert.Equal(TimeSpan.Zero, first.CreatedAt.Offset);
        Assert.Equal(8, Convert.FromBase64String(first.RowVersion).Length);
        Assert.Equal(first, await service.GetAsync(first.Id, Ct));
        var page = await service.ListAsync(new() { Page = 2, PageSize = 1, SortBy = "code" }, Ct);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(second.Id, Assert.Single(page.Items).Id);
        Assert.Equal(first.Id, Assert.Single((await service.ListAsync(new() { Search = "cat-a" }, Ct)).Items).Id);
        var updated = await service.UpdateAsync(first.Id, new() { Code = first.Code, Name = "Updated", RowVersion = first.RowVersion }, Ct);
        Assert.Equal("Updated", updated.Name);
        Assert.Equal(first.CreatedAt, updated.CreatedAt);
        Assert.Equal(fixture.ActorId, updated.UpdatedByUserId);
        Assert.NotEqual(first.RowVersion, updated.RowVersion);
        await Conflict(() => service.UpdateAsync(first.Id, new() { Code = first.Code, Name = "stale", RowVersion = first.RowVersion }, Ct));
        await using var fresh = fixture.Context();
        var inactive = await fixture.Suppliers(fresh).DeactivateAsync(first.Id, updated.RowVersion, Ct);
        Assert.False(inactive.IsActive);
        Assert.Equal(first.Id, Assert.Single((await fixture.Suppliers(fresh).ListAsync(new() { IsActive = false }, Ct)).Items).Id);
        Assert.Equal(2, await fresh.Suppliers.CountAsync());
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => service.GetAsync(int.MaxValue, Ct))).StatusCode);
        await Conflict(() => service.CreateAsync(new() { Code = " cat-b ", Name = "Duplicate" }, Ct));
    }

    [PartnersSqlFact]
    public async Task ConcurrentDuplicateSupplierReturnsOneSuccessAndOneConflict()
    {
        await using var fixture = await PartnersSqlFixture.OpenAsync();
        var barrier = new TwoSaves();
        await using var first = fixture.Context(barrier);
        await using var second = fixture.Context(barrier);
        async Task<int> Create(BttMiniERP.Api.Data.ApplicationDbContext db)
        {
            try { await fixture.Suppliers(db).CreateAsync(new() { Code = "same", Name = "Supplier" }, Ct); return 201; }
            catch (ApiException error) { return error.StatusCode; }
        }
        Assert.Equal(new[] { 201, 409 }, (await Task.WhenAll(Create(first), Create(second))).Order());
        await using var verify = fixture.Context();
        Assert.Equal(1, await verify.Suppliers.CountAsync());
    }

    [PartnersSqlFact]
    public async Task CustomerCrudNormalizesFiltersPaginatesAndPreservesAudit()
    {
        await using var fixture = await PartnersSqlFixture.OpenAsync();
        await using var db = fixture.Context();
        var service = fixture.Customers(db);
        var first = await service.CreateAsync(new() { Code = " cat-a ", Name = " First ", Phone = " 0123456789 ", Email = "a@example.com", Address = " Address " }, Ct);
        var second = await service.CreateAsync(new() { Code = "cat-b", Name = "Second" }, Ct);
        Assert.Equal("CAT-A", first.Code);
        Assert.Equal("First", first.Name);
        Assert.Equal("0123456789", first.Phone);
        Assert.Equal("a@example.com", first.Email);
        Assert.Equal("Address", first.Address);
        Assert.Equal(fixture.ActorId, first.CreatedByUserId);
        Assert.Equal(TimeSpan.Zero, first.CreatedAt.Offset);
        Assert.Equal(8, Convert.FromBase64String(first.RowVersion).Length);
        Assert.Equal(first, await service.GetAsync(first.Id, Ct));
        var page = await service.ListAsync(new() { Page = 2, PageSize = 1, SortBy = "code" }, Ct);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(second.Id, Assert.Single(page.Items).Id);
        Assert.Equal(first.Id, Assert.Single((await service.ListAsync(new() { Search = "cat-a" }, Ct)).Items).Id);
        var updated = await service.UpdateAsync(first.Id, new() { Code = first.Code, Name = "Updated", RowVersion = first.RowVersion }, Ct);
        Assert.Equal("Updated", updated.Name);
        Assert.Equal(first.CreatedAt, updated.CreatedAt);
        Assert.Equal(fixture.ActorId, updated.UpdatedByUserId);
        Assert.NotEqual(first.RowVersion, updated.RowVersion);
        await Conflict(() => service.UpdateAsync(first.Id, new() { Code = first.Code, Name = "stale", RowVersion = first.RowVersion }, Ct));
        await using var fresh = fixture.Context();
        var inactive = await fixture.Customers(fresh).DeactivateAsync(first.Id, updated.RowVersion, Ct);
        Assert.False(inactive.IsActive);
        Assert.Equal(first.Id, Assert.Single((await fixture.Customers(fresh).ListAsync(new() { IsActive = false }, Ct)).Items).Id);
        Assert.Equal(2, await fresh.Customers.CountAsync());
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => service.GetAsync(int.MaxValue, Ct))).StatusCode);
        await Conflict(() => service.CreateAsync(new() { Code = " cat-b ", Name = "Duplicate" }, Ct));
    }

    [PartnersSqlFact]
    public async Task ConcurrentDuplicateCustomerReturnsOneSuccessAndOneConflict()
    {
        await using var fixture = await PartnersSqlFixture.OpenAsync();
        var barrier = new TwoSaves();
        await using var first = fixture.Context(barrier);
        await using var second = fixture.Context(barrier);
        async Task<int> Create(BttMiniERP.Api.Data.ApplicationDbContext db)
        {
            try { await fixture.Customers(db).CreateAsync(new() { Code = "same", Name = "Customer" }, Ct); return 201; }
            catch (ApiException error) { return error.StatusCode; }
        }
        Assert.Equal(new[] { 201, 409 }, (await Task.WhenAll(Create(first), Create(second))).Order());
        await using var verify = fixture.Context();
        Assert.Equal(1, await verify.Customers.CountAsync());
    }


    private static async Task Conflict(Func<Task> action) => Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(action)).StatusCode);
    private sealed class TwoSaves : SaveChangesInterceptor
    {
        private int arrived;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref arrived) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            return result;
        }
    }


}