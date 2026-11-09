using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.DTOs.Suppliers;
using BttMiniERP.Api.DTOs.MasterData;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BttMiniERP.Api.Controllers;

[ApiController, Authorize(Policy = AppRoles.ReadSuppliers)]
[Route("api/v1/suppliers")]
public sealed class SuppliersController(SupplierService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SupplierPage>> List([FromQuery] MasterDataQuery query, CancellationToken cancellationToken)
        => Ok(await service.ListAsync(query, cancellationToken));

    [HttpGet("{id:int:min(1)}")]
    public async Task<ActionResult<SupplierResponse>> Get(int id, CancellationToken cancellationToken)
        => Ok(await service.GetAsync(id, cancellationToken));

    [HttpPost, Authorize(Policy = AppRoles.ManageSuppliers)]
    public async Task<ActionResult<SupplierResponse>> Create(CreateSupplierRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:int:min(1)}"), Authorize(Policy = AppRoles.ManageSuppliers)]
    public async Task<ActionResult<SupplierResponse>> Update(int id, UpdateSupplierRequest request, CancellationToken cancellationToken)
        => Ok(await service.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:int:min(1)}"), Authorize(Policy = AppRoles.ManageSuppliers)]
    public async Task<ActionResult<SupplierResponse>> Deactivate(int id, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken)
        => Ok(await service.DeactivateAsync(id, MasterDataRules.ParseIfMatch(ifMatch), cancellationToken));
}
