using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.DTOs.ProductionOrders;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BttMiniERP.Api.Controllers;

[ApiController, Route("api/v1/production-orders"), Authorize(Policy = AppRoles.ReadProductionOrders)]
public sealed class ProductionOrdersController(ProductionOrderService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ProductionOrderPage>> List([FromQuery] ProductionOrderQuery query, CancellationToken ct)
        => Ok(await service.ListAsync(query, ct));
    [HttpGet("{id:int:min(1)}")]
    public async Task<ActionResult<ProductionOrderResponse>> Get(int id, CancellationToken ct) => Ok(await service.GetAsync(id, ct));
    [HttpPost, Authorize(Policy = AppRoles.ManageProductionOrders)]
    public async Task<ActionResult<ProductionOrderResponse>> Create(CreateProductionOrderRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }
    [HttpPut("{id:int:min(1)}"), Authorize(Policy = AppRoles.ManageProductionOrders)]
    public async Task<ActionResult<ProductionOrderResponse>> Update(int id, UpdateProductionOrderRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));
    [HttpPost("{id:int:min(1)}/cancel"), Authorize(Policy = AppRoles.ManageProductionOrders)]
    public async Task<ActionResult<ProductionOrderResponse>> Cancel(int id, CancelProductionOrderRequest request, CancellationToken ct)
        => Ok(await service.CancelAsync(id, request.RowVersion, ct));
}
