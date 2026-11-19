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
/// Security regression: a JWT refresh token (same signing key, issuer and audience as the access token, marked with
/// token_type=refresh) must not be accepted as an access token. It used to be - granting API access for the whole
/// refresh-token lifetime (7 days by default).
/// </summary>
public class JwtRefreshTokenAsAccessTokenTests : IAsyncLifetime
{
    private static readonly JwtTokenConfig Config = new()
    {
        Scheme = JwtBearerDefaults.AuthenticationScheme,
        Secret = "a-test-secret-that-is-at-least-32-characters-long",
        Expire = TimeSpan.FromMinutes(10),
        RefreshExpire = TimeSpan.FromDays(7),
    };

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication().AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.TokenValidationParameters = Config.GetTokenValidationParameters();
            options.Events = JwtTokenConfig.CreateBearerEvents(); // what the npgsqlrest client registers
        });
        builder.Services.AddAuthorization();
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapGet("/me", (HttpContext ctx) => ctx.User.FindFirst("user_id")?.Value).RequireAuthorization();
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static (string access, string refresh) Tokens()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("user_id", "42")], "test"));
        var (access, refresh, _, _) = new JwtTokenGenerator(Config).GenerateTokens(principal);
        return (access, refresh);
    }

    private async Task<HttpResponseMessage> Get(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Access_token_is_accepted()
    {
        using var response = await Get(Tokens().access);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("42");
    }

    [Fact]
    public async Task Refresh_token_is_rejected_as_access_token()
    {
        using var response = await Get(Tokens().refresh);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
