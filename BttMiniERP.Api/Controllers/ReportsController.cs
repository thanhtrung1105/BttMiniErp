using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.DTOs.Reports;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BttMiniERP.Api.Controllers;

[ApiController, Route("api/v1/reports")]
public sealed class ReportsController(ReportService service) : ControllerBase
{
    [HttpGet("purchases"), Authorize(Policy = AppRoles.ReadReports)]
    public Task<ReportPage<PurchaseRow>> Purchases([FromQuery] PurchaseReportQuery query, CancellationToken ct)
        => service.PurchasesAsync(query, ct);

    [HttpGet("sales"), Authorize(Policy = AppRoles.ReadReports)]
    public Task<ReportPage<SalesRow>> Sales([FromQuery] SalesReportQuery query, CancellationToken ct)
        => service.SalesAsync(query, ct);

    [HttpGet("inventory"), Authorize(Policy = AppRoles.ReadInventoryReport)]
    public Task<ReportPage<InventoryReportRow>> Inventory([FromQuery] InventoryReportQuery query, CancellationToken ct)
        => service.InventoryAsync(query, ct);

    [HttpGet("production"), Authorize(Policy = AppRoles.ReadReports)]
    public Task<ReportPage<ProductionRow>> Production([FromQuery] ProductionReportQuery query, CancellationToken ct)
        => service.ProductionAsync(query, ct);
}
