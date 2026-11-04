using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.DTOs.Users;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BttMiniERP.Api.Controllers;

[ApiController, Authorize(Policy = AppRoles.ManageUsers)]
[Route("api/v1/users")]
public sealed class UsersController(UserService users) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<UserPage>> List([FromQuery] UserQuery query, CancellationToken cancellationToken)
        => Ok(await users.ListAsync(query, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await users.CreateAsync(request, cancellationToken);
        return Created($"/api/v1/users?id={Uri.EscapeDataString(user.Id)}", user);
    }

    [HttpPut("{id}/roles")]
    public async Task<ActionResult<UserResponse>> SetRoles(string id, SetRolesRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128) return BadRequest(new ProblemDetails { Status = 400, Title = "ID tài khoản không hợp lệ." });
        return Ok(await users.SetRolesAsync(id, request, cancellationToken));
    }
}
