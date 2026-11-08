using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.DTOs.Categories;
using BttMiniERP.Api.DTOs.MasterData;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BttMiniERP.Api.Controllers;

[ApiController, Authorize(Policy = AppRoles.ReadMasterData)]
[Route("api/v1/categories")]
public sealed class CategoriesController(CategoryService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CategoryPage>> List([FromQuery] MasterDataQuery query, CancellationToken cancellationToken)
        => Ok(await service.ListAsync(query, cancellationToken));

    [HttpGet("{id:int:min(1)}")]
    public async Task<ActionResult<CategoryResponse>> Get(int id, CancellationToken cancellationToken)
        => Ok(await service.GetAsync(id, cancellationToken));

    [HttpPost, Authorize(Policy = AppRoles.ManageMasterData)]
    public async Task<ActionResult<CategoryResponse>> Create(CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:int:min(1)}"), Authorize(Policy = AppRoles.ManageMasterData)]
    public async Task<ActionResult<CategoryResponse>> Update(int id, UpdateCategoryRequest request, CancellationToken cancellationToken)
        => Ok(await service.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:int:min(1)}"), Authorize(Policy = AppRoles.ManageMasterData)]
    public async Task<ActionResult<CategoryResponse>> Deactivate(int id, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken)
        => Ok(await service.DeactivateAsync(id, MasterDataRules.ParseIfMatch(ifMatch), cancellationToken));
}
