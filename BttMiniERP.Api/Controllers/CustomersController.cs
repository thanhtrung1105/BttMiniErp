using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.DTOs.Customers;
using BttMiniERP.Api.DTOs.MasterData;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BttMiniERP.Api.Controllers;

[ApiController, Authorize(Policy = AppRoles.ReadCustomers)]
[Route("api/v1/customers")]
public sealed class CustomersController(CustomerService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CustomerPage>> List([FromQuery] MasterDataQuery query, CancellationToken cancellationToken)
        => Ok(await service.ListAsync(query, cancellationToken));

    [HttpGet("{id:int:min(1)}")]
    public async Task<ActionResult<CustomerResponse>> Get(int id, CancellationToken cancellationToken)
        => Ok(await service.GetAsync(id, cancellationToken));

    [HttpPost, Authorize(Policy = AppRoles.ManageCustomers)]
    public async Task<ActionResult<CustomerResponse>> Create(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:int:min(1)}"), Authorize(Policy = AppRoles.ManageCustomers)]
    public async Task<ActionResult<CustomerResponse>> Update(int id, UpdateCustomerRequest request, CancellationToken cancellationToken)
        => Ok(await service.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:int:min(1)}"), Authorize(Policy = AppRoles.ManageCustomers)]
    public async Task<ActionResult<CustomerResponse>> Deactivate(int id, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken)
        => Ok(await service.DeactivateAsync(id, MasterDataRules.ParseIfMatch(ifMatch), cancellationToken));
}
