using BttMiniERP.Api.DTOs.Suppliers;
using BttMiniERP.Api.DTOs.Customers;
using BttMiniERP.Api.DTOs.MasterData;
using BttMiniERP.Api.Common;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace BttMiniERP.Tests.Fixtures;

// Chỉ chứng minh HTTP binding/validation/policy/response. Không giả lập bằng chứng persistence.
public sealed class PartnersApiFactory : ApiFactory
{
    public int Calls;
    public int? ErrorStatus;
    public MasterDataQuery? LastQuery;
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.AddScoped<SupplierService>(_ => new Suppliers(this));
            services.AddScoped<CustomerService>(_ => new Customers(this));
        });
    }
    private sealed class Suppliers(PartnersApiFactory factory) : SupplierService(null!, null!, null!)
    {
        private SupplierResponse Response(bool active = true)
        {
            Interlocked.Increment(ref factory.Calls);
            if (factory.ErrorStatus is int status) throw new ApiException(status, "Lỗi test có kiểm soát.");
            return new(1, "CAT", "Supplier", null, null, null, null, active, DateTimeOffset.UnixEpoch, "test-actor", null, null, "AAAAAAAAAAA=");
        }
        public override Task<SupplierPage> ListAsync(MasterDataQuery query, CancellationToken ct) { factory.LastQuery = query; return Task.FromResult(new SupplierPage([Response()], query.Page, query.PageSize, 1)); }
        public override Task<SupplierResponse> GetAsync(int id, CancellationToken ct) => Task.FromResult(Response());
        public override Task<SupplierResponse> CreateAsync(CreateSupplierRequest request, CancellationToken ct) => Task.FromResult(Response());
        public override Task<SupplierResponse> UpdateAsync(int id, UpdateSupplierRequest request, CancellationToken ct) => Task.FromResult(Response());
        public override Task<SupplierResponse> DeactivateAsync(int id, string version, CancellationToken ct) => Task.FromResult(Response(false));
    }
    private sealed class Customers(PartnersApiFactory factory) : CustomerService(null!, null!, null!)
    {
        private CustomerResponse Response(bool active = true)
        {
            Interlocked.Increment(ref factory.Calls);
            if (factory.ErrorStatus is int status) throw new ApiException(status, "Lỗi test có kiểm soát.");
            return new(1, "ITEM", "Customer", null, null, null, active,
                DateTimeOffset.UnixEpoch, "test-actor", null, null, "AAAAAAAAAAA=");
        }
        public override Task<CustomerPage> ListAsync(MasterDataQuery query, CancellationToken ct) { factory.LastQuery = query; return Task.FromResult(new CustomerPage([Response()], query.Page, query.PageSize, 1)); }
        public override Task<CustomerResponse> GetAsync(int id, CancellationToken ct) => Task.FromResult(Response());
        public override Task<CustomerResponse> CreateAsync(CreateCustomerRequest request, CancellationToken ct) => Task.FromResult(Response());
        public override Task<CustomerResponse> UpdateAsync(int id, UpdateCustomerRequest request, CancellationToken ct) => Task.FromResult(Response());
        public override Task<CustomerResponse> DeactivateAsync(int id, string version, CancellationToken ct) => Task.FromResult(Response(false));
    }
}
