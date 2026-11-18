using BttMiniERP.Api.Common;
using BttMiniERP.Api.DTOs.SalesOrders;
using BttMiniERP.Api.Enums;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
namespace BttMiniERP.Tests.Fixtures;

// HTTP contract double; không phải bằng chứng transaction, FK hoặc kho SQL.
public sealed class SalesOrdersApiFactory : ApiFactory
{
    public int Calls; public int? ErrorStatus; public SalesOrderQuery? LastQuery;
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder); builder.ConfigureServices(s => s.AddScoped<SalesOrderService>(_ => new Double(this)));
    }
    private sealed class Double(SalesOrdersApiFactory f) : SalesOrderService(null!, null!, null!, null!)
    {
        private SalesOrderResponse Result(DocumentStatus status = DocumentStatus.Draft)
        {
            f.Calls++; if (f.ErrorStatus is int error) throw new ApiException(error, "Lỗi test có kiểm soát.");
            return new(1, "SO-TEST", 1, DateTimeOffset.UnixEpoch, status, 0, null, DateTimeOffset.UnixEpoch, "test-actor", null, null,
                status == DocumentStatus.Cancelled ? DateTimeOffset.UnixEpoch : null, status == DocumentStatus.Cancelled ? "test-actor" : null, "AAAAAAAAAAA=", []);
        }
        public override Task<SalesOrderResponse> GetAsync(int id, CancellationToken ct) => Task.FromResult(Result());
        public override Task<SalesOrderResponse> CreateAsync(CreateSalesOrderRequest r, CancellationToken ct) { SalesOrderRules.Calculate(r); return Task.FromResult(Result()); }
        public override Task<SalesOrderResponse> UpdateAsync(int id, UpdateSalesOrderRequest r, CancellationToken ct) { SalesOrderRules.Calculate(r); return Task.FromResult(Result()); }
        public override Task<SalesOrderResponse> CancelAsync(int id, string version, CancellationToken ct) => Task.FromResult(Result(DocumentStatus.Cancelled));
        public override Task<SalesOrderResponse> ConfirmAsync(int id, string version, CancellationToken ct) => Task.FromResult(Result(DocumentStatus.Confirmed));
        public override Task<SalesOrderPage> ListAsync(SalesOrderQuery q, CancellationToken ct)
        { f.LastQuery = q; Result(); return Task.FromResult(new SalesOrderPage([], q.Page, q.PageSize, 0)); }
    }
}
