using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using BttMiniERP.Api.Common;
using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using BttMiniERP.Api.DTOs.Users;

namespace BttMiniERP.Tests.Unit.Auth;

public sealed class SecurityRulesTests
{
    [Fact]
    public void ClaimsContainOnlyPublicIdentityAndWhitelistedRoles()
    {
        var settings = new JwtSettings { Issuer = "unit", Audience = "unit-client", SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };
        var user = new ApplicationUser { Id = "user-123", UserName = "student", PasswordHash = "never-return-hash", SecurityStamp = "never-return-stamp" };
        var response = new TokenService(Options.Create(settings), TimeProvider.System).Create(user, AppRoles.All);
        var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(response.AccessToken, settings.ValidationParameters(), out _);
        Assert.Equal(user.Id, principal.FindFirstValue("sub"));
        Assert.Equal(AppRoles.All.Order(), principal.FindAll("role").Select(claim => claim.Value).Order());
        Assert.DoesNotContain(principal.Claims, claim => claim.Value.StartsWith("never-return"));
        Assert.InRange(response.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(16));
    }

    [Theory]
    [InlineData("admin")][InlineData("Root")][InlineData("")]
    public void RejectsUnknownOrWrongCaseRoles(string role)
        => Assert.Throws<ApiException>(() => AppRoles.Validate([role]));

    [Fact]
    public void RejectsDuplicateRoles() => Assert.Throws<ApiException>(() => AppRoles.Validate(["Viewer", "Viewer"]));

    [Fact]
    public void MissingRolesAreRejectedButExplicitEmptyRolesStillProtectLastAdmin()
    {
        Assert.Equal(400, Assert.Throws<ApiException>(() => AppRoles.Validate(null)).StatusCode);
        Assert.Empty(AppRoles.Validate([]));
        Assert.Equal(409, Assert.Throws<ApiException>(() => AppRoles.EnsureAdminRemains(true, AppRoles.Validate([]), 1)).StatusCode);
    }

    [Fact]
    public async Task MissingRolesAreRejectedByRealUserServiceBeforePersistence()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext
        { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "admin")], "Bearer")) } };
        var service = new UserService(null!, null!, new CurrentUser(accessor), NullLogger<UserService>.Instance);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => service.CreateAsync(
            new CreateUserRequest { UserName = "student", Password = "Valid-Password!7382" }, default))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => service.SetRolesAsync("target", new SetRolesRequest(), default))).StatusCode);
    }

    [Fact]
    public void LastAdminCannotBeRemovedButCanRetainAdmin()
    {
        Assert.Equal(409, Assert.Throws<ApiException>(() => AppRoles.EnsureAdminRemains(true, ["Viewer"], 1)).StatusCode);
        AppRoles.EnsureAdminRemains(true, ["Admin", "Viewer"], 1);
        AppRoles.EnsureAdminRemains(true, ["Viewer"], 2);
        AppRoles.EnsureAdminRemains(false, [], 1);
    }

    [Fact]
    public void CurrentUserRequiresAuthenticatedSubject()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        Assert.Throws<ApiException>(() => new CurrentUser(accessor).Id);
        accessor.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "actual-id")], "Bearer"));
        Assert.Equal("actual-id", new CurrentUser(accessor).Id);
    }

    [Theory]
    [InlineData("")][InlineData("not-base64")][InlineData("YQ==")]
    public void InvalidSigningKeysFailValidation(string key)
        => Assert.False(new JwtSettings { Issuer = "unit", Audience = "unit", SigningKey = key }.IsValid());

    [Fact]
    public void MissingSecretsFailClosedAndIdentityOptionsKeep128()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationSecurity(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<JwtSettings>>().Value);
        var options = provider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        Assert.Equal(128, options.Stores.MaxLengthForKeys);
        Assert.Equal(IdentitySchemaVersions.Version1, options.Stores.SchemaVersion);
        Assert.Equal(5, options.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(12, options.Password.RequiredLength);
    }

    [Fact]
    public void BootstrapRequiresExplicitTargetWithoutExposingConnection()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=BttMiniERP_Dev;Integrated Security=True" }).Build();
        Assert.Throws<InvalidOperationException>(() => IdentitySeeder.ValidateTarget(config, "Development", []));
        Assert.Throws<InvalidOperationException>(() => IdentitySeeder.ValidateTarget(config, "Development",
            ["--seed-identity=true", "--approved-server=other", "--approved-database=BttMiniERP_Dev"]));
        IdentitySeeder.ValidateTarget(config, "Development", ["--seed-identity=true", "--approved-server=localhost", "--approved-database=BttMiniERP_Dev"]);
    }
}
