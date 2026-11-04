using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using BttMiniERP.Api.DTOs.Auth;
using BttMiniERP.Tests.Fixtures;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using BttMiniERP.Api.Entities;

namespace BttMiniERP.Tests.Integration.Auth;

public sealed class SecurityHttpTests
{
    private const string Password = "Only-For-Tests!4739";

    [Fact]
    public async Task LoginIssuesUsableTokenWithoutExposingSecretsAndUsesRealSubject()
    {
        await using var factory = new SecurityApiFactory();
        var user = await factory.AddUserAsync("admin-test", Password, "Admin");
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = "admin-test", password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Password, body);
        Assert.DoesNotContain("passwordHash", body);
        var token = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
        var list = await client.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains(user.Id, await list.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownWrongAndLockedLoginsAreIndistinguishableAndLockoutExpires()
    {
        await using var factory = new SecurityApiFactory();
        var user = await factory.AddUserAsync("known-user", Password, "Viewer");
        using var client = factory.CreateClient();
        var unknown = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = "missing-user", password = Password });
        var expectedTitle = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync()).RootElement.GetProperty("title").GetString();
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        for (var i = 0; i < 5; i++)
        {
            var wrong = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = "known-user", password = "wrong" });
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            Assert.Equal(expectedTitle, JsonDocument.Parse(await wrong.Content.ReadAsStringAsync()).RootElement.GetProperty("title").GetString());
        }
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        user = (await manager.FindByIdAsync(user.Id))!;
        Assert.True(user.LockoutEnd > DateTimeOffset.UtcNow);
        var locked = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = "known-user", password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal(expectedTitle, JsonDocument.Parse(await locked.Content.ReadAsStringAsync()).RootElement.GetProperty("title").GetString());
        Assert.True((await manager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddSeconds(-1))).Succeeded);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = "known-user", password = Password })).StatusCode);
        Assert.Equal(0, (await manager.FindByIdAsync(user.Id))!.AccessFailedCount);
    }

    [Fact]
    public async Task LoginRateLimitReturns429WithoutDatabaseAccess()
    {
        await using var factory = new SecurityApiFactory();
        using var client = factory.CreateClient();
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/login", new { })).StatusCode);
        var rejected = await client.PostAsJsonAsync("/api/v1/auth/login", new { });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task MissingTokenCannotManageUsersAndRoleUnionAllowsAdmin()
    {
        await using var factory = new SecurityApiFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/users", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync("/api/v1/users/id/roles", new { roles = new[] { "Viewer" } })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/users")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token("Viewer", "Admin"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/users")).StatusCode);
    }

    [Theory]
    [InlineData("Manager")][InlineData("PurchasingStaff")][InlineData("SalesStaff")]
    [InlineData("ProductionStaff")][InlineData("WarehouseStaff")][InlineData("Viewer")]
    public async Task NonAdminCannotReadCreateOrAssignUsers(string role)
    {
        await using var factory = new SecurityApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token(role));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/users", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/v1/users/target/roles", new { roles = new[] { "Admin" } })).StatusCode);
    }

    [Theory]
    [InlineData("missing")][InlineData("expired")][InlineData("issuer")][InlineData("audience")]
    [InlineData("signature")][InlineData("unsigned")][InlineData("algorithm")][InlineData("subject")][InlineData("future")]
    public async Task InvalidTokensReturnSafe401(string kind)
    {
        await using var factory = new SecurityApiFactory();
        using var client = factory.CreateClient();
        var claims = new List<Claim> { new("role", "Admin") };
        if (kind != "subject") claims.Add(new("sub", "actor"));
        var key = kind == "signature" ? new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) : factory.Jwt.GetKey();
        var credentials = kind == "unsigned" ? null : new SigningCredentials(key,
            kind == "algorithm" ? SecurityAlgorithms.HmacSha384 : SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(kind == "issuer" ? "wrong" : factory.Jwt.Issuer,
            kind == "audience" ? "wrong" : factory.Jwt.Audience, claims,
            DateTime.UtcNow.AddMinutes(kind == "future" ? 5 : -20), DateTime.UtcNow.AddMinutes(kind == "expired" ? -2 : 10), credentials);
        if (kind != "missing") client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        var response = await client.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("IDX", body);
        Assert.DoesNotContain(factory.Jwt.SigningKey, body);
        Assert.Contains("traceId", body);
    }

    [Fact]
    public async Task AdminContractsValidateRolesPaginationAndOverposting()
    {
        await using var factory = new SecurityApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token("Admin"));
        var created = await client.PostAsJsonAsync("/api/v1/users", new { userName = "new-user", password = Password, roles = new[] { "Viewer" } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Contains("?id=", created.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/v1/users/test/roles", new { roles = new[] { "Manager" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/users/test/roles", new { roles = new[] { "Root" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/users?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/users", new { userName = "bad", password = Password, roles = new[] { "Viewer" }, passwordHash = "injected" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/v1/auth/register", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { })).StatusCode);
    }

    [Fact]
    public async Task OpenApiDescribesBearerOnlyForProtectedOperations()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        Assert.Equal("bearer", document.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer").GetProperty("scheme").GetString());
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/v1/users").GetProperty("get").GetProperty("security").GetArrayLength() > 0);
        Assert.False(paths.GetProperty("/api/v1/auth/login").GetProperty("post").TryGetProperty("security", out _));
    }

    [Theory]
    [InlineData("missing", 400)]
    [InlineData("null", 400)]
    [InlineData("empty", 200)]
    [InlineData("valid", 200)]
    [InlineData("invalid", 400)]
    [InlineData("duplicate", 400)]
    [InlineData("unknown-field", 400)]
    public async Task CreateAndSetRolesRequireExplicitRoleArray(string kind, int expectedPut)
    {
        await using var factory = new SecurityApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token("Admin"));
        var body = new Dictionary<string, object?>();
        if (kind != "missing") body["roles"] = kind switch
        {
            "null" => null,
            "empty" => Array.Empty<string>(),
            "invalid" => new[] { "Root" },
            "duplicate" => new[] { "Viewer", "Viewer" },
            _ => new[] { "Viewer" }
        };
        if (kind == "unknown-field") body["actorId"] = "injected";
        using var put = await client.PutAsJsonAsync("/api/v1/users/target/roles", body);
        Assert.Equal(expectedPut, (int)put.StatusCode);
        body["userName"] = "new-user";
        body["password"] = Password;
        using var post = await client.PostAsJsonAsync("/api/v1/users", body);
        Assert.Equal(expectedPut == 200 ? 201 : 400, (int)post.StatusCode);
        if (expectedPut == 400)
        {
            Assert.Equal("application/problem+json", put.Content.Headers.ContentType?.MediaType);
            Assert.Equal("application/problem+json", post.Content.Headers.ContentType?.MediaType);
            if (kind is "missing" or "null")
            {
                using var error = JsonDocument.Parse(await put.Content.ReadAsStringAsync());
                Assert.True(error.RootElement.TryGetProperty("errors", out _));
            }
        }
    }

    [Fact]
    public async Task AlgorithmIsRejectedEvenWithCorrectKeyAndOtherwiseValidToken()
    {
        await using var factory = new SecurityApiFactory();
        var token = new JwtSecurityToken(factory.Jwt.Issuer, factory.Jwt.Audience,
            [new Claim("sub", "actor"), new Claim("role", "Admin")],
            DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow.AddMinutes(10),
            new SigningCredentials(factory.Jwt.GetKey(), SecurityAlgorithms.HmacSha384));
        var encoded = new JwtSecurityTokenHandler().WriteToken(token);
        var control = factory.Jwt.ValidationParameters();
        control.ValidAlgorithms = [SecurityAlgorithms.HmacSha384];
        // Chữ ký, key, issuer, audience, thời gian đều hợp lệ nếu cho HS384.
        new JwtSecurityTokenHandler().ValidateToken(encoded, control, out _);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", encoded);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/users")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token("Admin"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/users")).StatusCode);
    }
}
