using BttMiniERP.Api.Common;
using BttMiniERP.Api.DTOs.PurchaseReceipts;
using BttMiniERP.Api.Enums;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
namespace BttMiniERP.Tests.Fixtures;

// HTTP contract double; không phải bằng chứng transaction, FK hoặc kho SQL.
public sealed class PurchaseReceiptsApiFactory : ApiFactory
{
    public int Calls; public int? ErrorStatus; public PurchaseReceiptQuery? LastQuery;
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder); builder.ConfigureServices(s => s.AddScoped<PurchaseReceiptService>(_ => new Double(this)));
    }
    private sealed class Double(PurchaseReceiptsApiFactory f) : PurchaseReceiptService(null!, null!, null!, null!)
    {
        private PurchaseReceiptResponse Result(DocumentStatus status = DocumentStatus.Draft)
        {
            f.Calls++; if (f.ErrorStatus is int error) throw new ApiException(error, "Lỗi test có kiểm soát.");
            return new(1, "PR-TEST", 1, DateTimeOffset.UnixEpoch, status, 0, null, DateTimeOffset.UnixEpoch, "test-actor", null, null,
                status == DocumentStatus.Cancelled ? DateTimeOffset.UnixEpoch : null, status == DocumentStatus.Cancelled ? "test-actor" : null, "AAAAAAAAAAA=", []);
        }
        public override Task<PurchaseReceiptResponse> GetAsync(int id, CancellationToken ct) => Task.FromResult(Result());
        public override Task<PurchaseReceiptResponse> CreateAsync(CreatePurchaseReceiptRequest r, CancellationToken ct) { PurchaseReceiptRules.Calculate(r); return Task.FromResult(Result()); }
        public override Task<PurchaseReceiptResponse> UpdateAsync(int id, UpdatePurchaseReceiptRequest r, CancellationToken ct) { PurchaseReceiptRules.Calculate(r); return Task.FromResult(Result()); }
        public override Task<PurchaseReceiptResponse> CancelAsync(int id, string version, CancellationToken ct) => Task.FromResult(Result(DocumentStatus.Cancelled));
        public override Task<PurchaseReceiptResponse> ConfirmAsync(int id, string version, CancellationToken ct)
            => Task.FromResult(Result(DocumentStatus.Confirmed) with { ConfirmedAt = DateTimeOffset.UnixEpoch, ConfirmedByUserId = "test-actor" });
        public override Task<PurchaseReceiptPage> ListAsync(PurchaseReceiptQuery q, CancellationToken ct)
        { f.LastQuery=q; Result(); return Task.FromResult(new PurchaseReceiptPage([],q.Page,q.PageSize,0)); }
    }
}
