using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NpgsqlRestClient;

namespace NpgsqlRestTests.AuthTests;

/// <summary>
/// Regression: claims of a JWT sign-in must keep the names the login routine gave them. The JWT bearer handler
/// renames well-known claim types by default ("roles" and "role" to ClaimTypes.Role, "email" to ClaimTypes.Email),
/// so with DefaultRoleClaimType "roles" the role claim was not found: <c>authorize role</c> answered 403 for a JWT
/// that carried the role, and roles and email were NULL in user parameters and the user context.
/// </summary>
public class JwtClaimNamesTests : IAsyncLifetime
{
    private static readonly JwtTokenConfig Config = new()
    {
        Scheme = JwtBearerDefaults.AuthenticationScheme,
        Secret = "a-test-secret-that-is-at-least-32-characters-long",
        Expire = TimeSpan.FromMinutes(10),
        RefreshExpire = TimeSpan.FromDays(7),
    };

    private static readonly Claim[] LoginClaims =
    [
        new("user_id", "42"),
        new("username", "manager"),
        new("roles", "manager"),
        new("roles", "staff"),
        new("email", "manager@example.com"),
    ];

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        // what the npgsqlrest client registers
        builder.Services.AddAuthentication().AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, Config.ConfigureBearer);
        builder.Services.AddAuthorization();
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapGet("/claims", (HttpContext ctx) => string.Join(";", ctx.User.Claims
            .Where(c => c.Type is "user_id" or "username" or "roles" or "email")
            .Select(c => $"{c.Type}={c.Value}"))).RequireAuthorization();
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private const string Expected = "user_id=42;username=manager;roles=manager;roles=staff;email=manager@example.com";

    private async Task<string> GetClaims(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/claims");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Access_token_claims_keep_their_names()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(LoginClaims, "test"));
        var (access, _, _, _) = new JwtTokenGenerator(Config).GenerateTokens(principal);

        (await GetClaims(access)).Should().Be(Expected);
    }

    [Fact]
    public async Task Refreshed_token_claims_keep_their_names()
    {
        var generator = new JwtTokenGenerator(Config);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(LoginClaims, "test"));
        var (_, refresh, _, _) = generator.GenerateTokens(principal);

        // what the refresh endpoint does: validate the refresh token, issue new tokens from its claims
        var refreshed = generator.ValidateRefreshToken(refresh);
        refreshed.Should().NotBeNull();
        var claims = refreshed!.Claims.Where(c => c.Type != JwtTokenConfig.TokenTypeClaim).ToList();
        var (access, _, _, _) = generator.GenerateTokens(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")));

        (await GetClaims(access)).Should().Be(Expected);
    }
}
