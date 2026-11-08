using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.DTOs.Items;
using BttMiniERP.Api.DTOs.MasterData;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BttMiniERP.Api.Controllers;

[ApiController, Authorize(Policy = AppRoles.ReadMasterData)]
[Route("api/v1/items")]
public sealed class ItemsController(ItemService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ItemPage>> List([FromQuery] ItemQuery query, CancellationToken cancellationToken)
        => Ok(await service.ListAsync(query, cancellationToken));

    [HttpGet("{id:int:min(1)}")]
    public async Task<ActionResult<ItemResponse>> Get(int id, CancellationToken cancellationToken)
        => Ok(await service.GetAsync(id, cancellationToken));

    [HttpPost, Authorize(Policy = AppRoles.ManageMasterData)]
    public async Task<ActionResult<ItemResponse>> Create(CreateItemRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:int:min(1)}"), Authorize(Policy = AppRoles.ManageMasterData)]
    public async Task<ActionResult<ItemResponse>> Update(int id, UpdateItemRequest request, CancellationToken cancellationToken)
        => Ok(await service.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:int:min(1)}"), Authorize(Policy = AppRoles.ManageMasterData)]
    public async Task<ActionResult<ItemResponse>> Deactivate(int id, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken)
        => Ok(await service.DeactivateAsync(id, MasterDataRules.ParseIfMatch(ifMatch), cancellationToken));
}
