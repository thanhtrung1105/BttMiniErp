using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.DTOs.BillOfMaterials;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BttMiniERP.Api.Controllers;

[ApiController, Route("api/v1/bill-of-materials"), Authorize(Policy = AppRoles.ReadBillOfMaterials)]
public sealed class BillOfMaterialsController(BillOfMaterialService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<BillOfMaterialPage>> List([FromQuery] BillOfMaterialQuery query, CancellationToken ct)
        => Ok(await service.ListAsync(query, ct));
    [HttpGet("{id:int:min(1)}")]
    public async Task<ActionResult<BillOfMaterialResponse>> Get(int id, CancellationToken ct) => Ok(await service.GetAsync(id, ct));
    [HttpPost, Authorize(Policy = AppRoles.ManageBillOfMaterials)]
    public async Task<ActionResult<BillOfMaterialResponse>> Create(CreateBillOfMaterialRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }
    [HttpPut("{id:int:min(1)}"), Authorize(Policy = AppRoles.ManageBillOfMaterials)]
    public async Task<ActionResult<BillOfMaterialResponse>> Update(int id, UpdateBillOfMaterialRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));
    [HttpDelete("{id:int:min(1)}"), Authorize(Policy = AppRoles.ManageBillOfMaterials)]
    public async Task<ActionResult<BillOfMaterialResponse>> Deactivate(int id, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken ct)
        => Ok(await service.DeactivateAsync(id, MasterDataRules.ParseIfMatch(ifMatch), ct));
}
