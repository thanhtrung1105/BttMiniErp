using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.DTOs.SalesOrders;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace BttMiniERP.Api.Controllers;

[ApiController, Route("api/v1/sales-orders"), Authorize(Policy = AppRoles.ReadSalesOrders)]
public sealed class SalesOrdersController(SalesOrderService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SalesOrderPage>> List([FromQuery] SalesOrderQuery query, CancellationToken ct)
        => Ok(await service.ListAsync(query, ct));
    [HttpGet("{id:int:min(1)}")]
    public async Task<ActionResult<SalesOrderResponse>> Get(int id, CancellationToken ct) => Ok(await service.GetAsync(id, ct));
    [HttpPost, Authorize(Policy = AppRoles.ManageSalesOrders)]
    public async Task<ActionResult<SalesOrderResponse>> Create(CreateSalesOrderRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }
    [HttpPut("{id:int:min(1)}"), Authorize(Policy = AppRoles.ManageSalesOrders)]
    public async Task<ActionResult<SalesOrderResponse>> Update(int id, UpdateSalesOrderRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));
    [HttpPost("{id:int:min(1)}/confirm"), Authorize(Policy = AppRoles.ConfirmSalesOrders)]
    public async Task<ActionResult<SalesOrderResponse>> Confirm(int id, ConfirmSalesOrderRequest request, CancellationToken ct)
        => Ok(await service.ConfirmAsync(id, request.RowVersion, ct));

    [HttpPost("{id:int:min(1)}/cancel"), Authorize(Policy = AppRoles.ManageSalesOrders)]
    public async Task<ActionResult<SalesOrderResponse>> Cancel(int id, CancelSalesOrderRequest request, CancellationToken ct)
        => Ok(await service.CancelAsync(id, request.RowVersion, ct));
}
