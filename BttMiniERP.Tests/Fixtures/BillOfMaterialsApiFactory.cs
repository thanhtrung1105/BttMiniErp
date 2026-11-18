using BttMiniERP.Api.Common;
using BttMiniERP.Api.DTOs.BillOfMaterials;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace BttMiniERP.Tests.Fixtures;

// Chỉ kiểm chứng HTTP contract/policy; không thay bằng chứng SQL transaction.
public sealed class BillOfMaterialsApiFactory : ApiFactory
{
    public int Calls;
    public int? ErrorStatus;
    public string? LastVersion;
    public BillOfMaterialQuery? LastQuery;
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(s => s.AddScoped<BillOfMaterialService>(_ => new Double(this)));
    }
    private sealed class Double(BillOfMaterialsApiFactory f) : BillOfMaterialService(null!, null!, null!)
    {
        private BillOfMaterialResponse Result(bool active = true)
        {
            f.Calls++;
            if (f.ErrorStatus is int error) throw new ApiException(error, "Lỗi BOM kiểm thử.");
            return new(1, "BOM", 1, 1, active, null, DateTimeOffset.UnixEpoch, "test-actor", null, null,
                "AAAAAAAAAAA=", [new(1, 2, 1.125m)]);
        }
        public override Task<BillOfMaterialResponse> CreateAsync(CreateBillOfMaterialRequest r, CancellationToken ct)
        { BillOfMaterialRules.Details(r); return Task.FromResult(Result()); }
        public override Task<BillOfMaterialResponse> UpdateAsync(int id, UpdateBillOfMaterialRequest r, CancellationToken ct)
        { BillOfMaterialRules.Details(r); f.LastVersion = r.RowVersion; return Task.FromResult(Result()); }
        public override Task<BillOfMaterialResponse> DeactivateAsync(int id, string version, CancellationToken ct)
        { f.LastVersion = version; return Task.FromResult(Result(false)); }
        public override Task<BillOfMaterialResponse> GetAsync(int id, CancellationToken ct) => Task.FromResult(Result());
        public override Task<BillOfMaterialPage> ListAsync(BillOfMaterialQuery q, CancellationToken ct)
        { f.LastQuery = q; Result(); return Task.FromResult(new BillOfMaterialPage([], q.Page, q.PageSize, 0)); }
    }
}
