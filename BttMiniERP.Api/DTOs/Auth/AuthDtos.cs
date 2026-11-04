using System.ComponentModel.DataAnnotations;

namespace BttMiniERP.Api.DTOs.Auth;

public sealed class LoginRequest
{
    [Required, StringLength(256)] public string UserName { get; init; } = "";
    [Required, StringLength(128)] public string Password { get; init; } = "";
}

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, IReadOnlyList<string> Roles);
