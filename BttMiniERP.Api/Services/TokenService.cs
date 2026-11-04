using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.DTOs.Auth;
using BttMiniERP.Api.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BttMiniERP.Api.Services;

public sealed class TokenService(IOptions<JwtSettings> settings, TimeProvider clock)
{
    public LoginResponse Create(ApplicationUser user, IEnumerable<string> roles)
    {
        var allowedRoles = AppRoles.Validate(roles);
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(settings.Value.LifetimeMinutes);
        var claims = new List<Claim> { new("sub", user.Id), new("name", user.UserName ?? ""),
            new("jti", Guid.NewGuid().ToString()), new("iat", now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64) };
        claims.AddRange(allowedRoles.Select(role => new Claim("role", role)));
        var token = new JwtSecurityToken(settings.Value.Issuer, settings.Value.Audience, claims,
            now.UtcDateTime, expires.UtcDateTime, new SigningCredentials(settings.Value.GetKey(), SecurityAlgorithms.HmacSha256));
        return new(new JwtSecurityTokenHandler().WriteToken(token), expires, allowedRoles);
    }
}
