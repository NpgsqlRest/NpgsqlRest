using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NpgsqlRestClient;

namespace NpgsqlRestTests.AuthTests;

/// <summary>
/// Security regression for the BearerToken refresh endpoint (TokenRefreshAuth): new tokens must be issued for the
/// principal inside the refresh token. It used to sign in the caller (context.User) instead - a valid access token
/// plus any unexpired refresh token renewed the access token's owner, and refreshing with an expired access token
/// (the normal case) failed.
/// </summary>
public class BearerTokenRefreshTests : IAsyncLifetime
{
    private const string Scheme = BearerTokenDefaults.AuthenticationScheme;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication().AddBearerToken(Scheme);
        builder.Services.AddAuthorization();
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        new TokenRefreshAuth(new BearerTokenConfig { Scheme = Scheme, RefreshPath = "/bt-refresh" }, _app, null);
        _app.MapGet("/bt-login/{id}", (string id) => Results.SignIn(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("user_id", id)], Scheme)), authenticationScheme: Scheme));
        _app.MapGet("/me", (HttpContext ctx) => ctx.User.FindFirst("user_id")?.Value)
            .RequireAuthorization(policy => policy.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser());
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task<(string access, string refresh)> Login(string id)
    {
        var json = JsonDocument.Parse(await _client.GetStringAsync($"/bt-login/{id}")).RootElement;
        return (json.GetProperty("accessToken").GetString()!, json.GetProperty("refreshToken").GetString()!);
    }

    private async Task<HttpResponseMessage> Refresh(string refreshToken, string? accessToken = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bt-refresh")
        {
            Content = new StringContent($"{{\"refresh\":\"{refreshToken}\"}}", Encoding.UTF8, "application/json")
        };
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
        return await _client.SendAsync(request);
    }

    private async Task<string> Me(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Refresh_issues_tokens_for_the_refresh_token_owner_not_the_caller()
    {
        var victim = await Login("victim");
        var attacker = await Login("attacker");

        // victim's access token on the request, attacker's refresh token in the body
        using var response = await Refresh(attacker.refresh, accessToken: victim.access);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var newAccess = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("accessToken").GetString()!;
        (await Me(newAccess)).Should().Be("attacker");
    }

    [Fact]
    public async Task Refresh_works_without_an_access_token()
    {
        var user = await Login("user1");

        using var response = await Refresh(user.refresh);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var newAccess = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("accessToken").GetString()!;
        (await Me(newAccess)).Should().Be("user1");
    }

    [Fact]
    public async Task Invalid_refresh_token_is_rejected()
    {
        using var response = await Refresh("not-a-valid-refresh-token");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
