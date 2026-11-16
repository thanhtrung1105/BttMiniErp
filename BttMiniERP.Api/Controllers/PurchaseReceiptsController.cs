using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.DTOs.PurchaseReceipts;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace BttMiniERP.Api.Controllers;

[ApiController, Route("api/v1/purchase-receipts"), Authorize(Policy = AppRoles.ReadPurchaseReceipts)]
public sealed class PurchaseReceiptsController(PurchaseReceiptService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PurchaseReceiptPage>> List([FromQuery] PurchaseReceiptQuery query, CancellationToken ct)
        => Ok(await service.ListAsync(query, ct));
    [HttpGet("{id:int:min(1)}")]
    public async Task<ActionResult<PurchaseReceiptResponse>> Get(int id, CancellationToken ct) => Ok(await service.GetAsync(id, ct));
    [HttpPost, Authorize(Policy = AppRoles.ManagePurchaseReceipts)]
    public async Task<ActionResult<PurchaseReceiptResponse>> Create(CreatePurchaseReceiptRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }
    [HttpPut("{id:int:min(1)}"), Authorize(Policy = AppRoles.ManagePurchaseReceipts)]
    public async Task<ActionResult<PurchaseReceiptResponse>> Update(int id, UpdatePurchaseReceiptRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));
    [HttpPost("{id:int:min(1)}/cancel"), Authorize(Policy = AppRoles.ManagePurchaseReceipts)]
    public async Task<ActionResult<PurchaseReceiptResponse>> Cancel(int id, CancelPurchaseReceiptRequest request, CancellationToken ct)
        => Ok(await service.CancelAsync(id, request.RowVersion, ct));
    [HttpPost("{id:int:min(1)}/confirm"), Authorize(Policy = AppRoles.ConfirmPurchaseReceipts)]
    public async Task<ActionResult<PurchaseReceiptResponse>> Confirm(int id, ConfirmPurchaseReceiptRequest request, CancellationToken ct)
        => Ok(await service.ConfirmAsync(id, request.RowVersion, ct));
}
