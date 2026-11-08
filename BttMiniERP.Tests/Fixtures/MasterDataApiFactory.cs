using BttMiniERP.Api.DTOs.Categories;
using BttMiniERP.Api.DTOs.Items;
using BttMiniERP.Api.DTOs.MasterData;
using BttMiniERP.Api.Enums;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace BttMiniERP.Tests.Fixtures;

// Chỉ chứng minh HTTP binding/validation/policy/response. Không giả lập bằng chứng persistence.
public sealed class MasterDataApiFactory : ApiFactory
{
    public int Calls;
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.AddScoped<CategoryService>(_ => new Categories(this));
            services.AddScoped<ItemService>(_ => new Items(this));
        });
    }
    private sealed class Categories(MasterDataApiFactory factory) : CategoryService(null!, null!, null!)
    {
        private CategoryResponse Response(bool active = true)
        {
            Interlocked.Increment(ref factory.Calls);
            return new(1, "CAT", "Category", null, active, DateTimeOffset.UnixEpoch, "test-actor", null, null, "AAAAAAAAAAA=");
        }
        public override Task<CategoryPage> ListAsync(MasterDataQuery query, CancellationToken ct) => Task.FromResult(new CategoryPage([Response()], query.Page, query.PageSize, 1));
        public override Task<CategoryResponse> GetAsync(int id, CancellationToken ct) => Task.FromResult(Response());
        public override Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, CancellationToken ct) => Task.FromResult(Response());
        public override Task<CategoryResponse> UpdateAsync(int id, UpdateCategoryRequest request, CancellationToken ct) => Task.FromResult(Response());
        public override Task<CategoryResponse> DeactivateAsync(int id, string version, CancellationToken ct) => Task.FromResult(Response(false));
    }
    private sealed class Items(MasterDataApiFactory factory) : ItemService(null!, null!, null!)
    {
        private ItemResponse Response(bool active = true)
        {
            Interlocked.Increment(ref factory.Calls);
            return new(1, "ITEM", "Item", 1, "kg", ItemType.RawMaterial, 10, 20, active,
                DateTimeOffset.UnixEpoch, "test-actor", null, null, "AAAAAAAAAAA=");
        }
        public override Task<ItemPage> ListAsync(ItemQuery query, CancellationToken ct) => Task.FromResult(new ItemPage([Response()], query.Page, query.PageSize, 1));
        public override Task<ItemResponse> GetAsync(int id, CancellationToken ct) => Task.FromResult(Response());
        public override Task<ItemResponse> CreateAsync(CreateItemRequest request, CancellationToken ct) => Task.FromResult(Response());
        public override Task<ItemResponse> UpdateAsync(int id, UpdateItemRequest request, CancellationToken ct) => Task.FromResult(Response());
        public override Task<ItemResponse> DeactivateAsync(int id, string version, CancellationToken ct) => Task.FromResult(Response(false));
    }
}
