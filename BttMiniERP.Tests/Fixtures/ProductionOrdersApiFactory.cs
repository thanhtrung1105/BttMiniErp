using BttMiniERP.Api.Common;
using BttMiniERP.Api.DTOs.ProductionOrders;
using BttMiniERP.Api.Enums;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace BttMiniERP.Tests.Fixtures;

// Chỉ kiểm chứng HTTP/DTO/policy; không thay bằng chứng persistence SQL.
public sealed class ProductionOrdersApiFactory : ApiFactory
{
    public int Calls;
    public int? ErrorStatus;
    public ProductionOrderQuery? LastQuery;
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(s => s.AddScoped<ProductionOrderService>(_ => new Double(this)));
    }
    private sealed class Double(ProductionOrdersApiFactory f) : ProductionOrderService(null!, null!, null!, null!)
    {
        private ProductionOrderResponse Result(decimal quantity = 3, DocumentStatus status = DocumentStatus.Draft)
        {
            f.Calls++;
            if (f.ErrorStatus is int error) throw new ApiException(error, "Lỗi test có kiểm soát.");
            return new(1,"PO-TEST",1,2,10,quantity,status,null,DateTimeOffset.UnixEpoch,"test-actor",null,null,
                status==DocumentStatus.Cancelled ? DateTimeOffset.UnixEpoch : null,
                status==DocumentStatus.Cancelled ? "test-actor" : null,null,null,"AAAAAAAAAAA=",
                [new(1,2,2,ProductionOrderRules.RequiredQuantity(2,quantity),null)]);
        }
        public override Task<ProductionOrderResponse> GetAsync(int id,CancellationToken ct) => Task.FromResult(Result());
        public override Task<ProductionOrderResponse> CreateAsync(CreateProductionOrderRequest r,CancellationToken ct)
        { MasterDataRules.Validate(r); ProductionOrderRules.RequireQuantity(r.Quantity); return Task.FromResult(Result(r.Quantity)); }
        public override Task<ProductionOrderResponse> UpdateAsync(int id,UpdateProductionOrderRequest r,CancellationToken ct)
        { MasterDataRules.Validate(r); ProductionOrderRules.RequireQuantity(r.Quantity); return Task.FromResult(Result(r.Quantity)); }
        public override Task<ProductionOrderResponse> CancelAsync(int id,string version,CancellationToken ct)
        { MasterDataRules.ParseVersion(version); return Task.FromResult(Result(status:DocumentStatus.Cancelled)); }
        public override Task<ProductionOrderResponse> ConfirmAsync(int id, string version, CancellationToken ct)
        {
            MasterDataRules.ParseVersion(version);
            return Task.FromResult(Result(status: DocumentStatus.Confirmed));
        }
        public override Task<ProductionOrderPage> ListAsync(ProductionOrderQuery q,CancellationToken ct)
        { MasterDataRules.Validate(q); f.LastQuery=q; Result(); return Task.FromResult(new ProductionOrderPage([],q.Page,q.PageSize,0)); }
    }
}
