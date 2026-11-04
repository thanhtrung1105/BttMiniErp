using Microsoft.IdentityModel.Tokens;

namespace BttMiniERP.Api.Common.Authorization;

public sealed class JwtSettings
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SigningKey { get; set; } = "";
    public int LifetimeMinutes { get; set; } = 15;

    public bool IsValid()
    {
        try { return !string.IsNullOrWhiteSpace(Issuer) && !string.IsNullOrWhiteSpace(Audience)
            && LifetimeMinutes is >= 5 and <= 30 && Convert.FromBase64String(SigningKey).Length >= 32; }
        catch (FormatException) { return false; }
    }

    public SymmetricSecurityKey GetKey() => new(Convert.FromBase64String(SigningKey));
    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true, ValidIssuer = Issuer, ValidateAudience = true, ValidAudience = Audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = GetKey(), RequireSignedTokens = true,
        ValidateLifetime = true, RequireExpirationTime = true, ClockSkew = TimeSpan.FromSeconds(30),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256], NameClaimType = "name", RoleClaimType = "role"
    };
}
