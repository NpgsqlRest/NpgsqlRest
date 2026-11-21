using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NpgsqlRestClient;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void JwtOnlyLogoutTests()
    {
        script.Append("""
create function jwtonly_logout() returns void language sql as 'select 1';
comment on function jwtonly_logout() is 'HTTP POST
logout';
create function jwtonly_logout_named() returns table (scheme text) language sql as $$ values ('Bearer') $$;
comment on function jwtonly_logout_named() is 'HTTP POST
logout';
create function jwtonly_ping() returns text language sql as $$ select 'pong' $$;
comment on function jwtonly_ping() is 'HTTP GET
authorize';
""");
    }
}

/// <summary>
/// Logout in an application whose only authentication scheme is JWT. A JWT scheme keeps no session on the server
/// and its handler cannot sign out, so a logout has nothing to do there: it must answer normally, signed in or not.
/// (4.0.1 covered the request that carries a valid token; a request without one still answered 500, because the
/// default sign-out scheme is the bearer handler too.)
/// </summary>
public class JwtOnlyLogoutTests : IAsyncLifetime
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
        var connectionString = Database.Create();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, Config.ConfigureBearer);
        builder.Services.AddAuthorization();
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseNpgsqlRest(new NpgsqlRestOptions(new Npgsql.NpgsqlConnectionStringBuilder(connectionString) { ApplicationName = "jwt-only-logout-tests" }.ConnectionString)
        {
            IncludeSchemas = ["public"],
            NameSimilarTo = "jwtonly[_]%",
            RequiresAuthorization = false,
        });
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static string Token()
    {
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("user_id", "1"), new System.Security.Claims.Claim("username", "u")], "test"));
        return new JwtTokenGenerator(Config).GenerateTokens(principal).accessToken;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? bearer)
    {
        var request = new HttpRequestMessage(method, path);
        if (bearer is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        }
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task The_token_is_accepted_by_this_application()
    {
        using var anonymous = await SendAsync(HttpMethod.Get, "/api/jwtonly-ping", null);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var signedIn = await SendAsync(HttpMethod.Get, "/api/jwtonly-ping", Token());
        signedIn.StatusCode.Should().Be(HttpStatusCode.OK);
        (await signedIn.Content.ReadAsStringAsync()).Should().Be("pong");
    }

    [Theory]
    [InlineData("/api/jwtonly-logout")]
    [InlineData("/api/jwtonly-logout-named")]
    public async Task Logout_without_a_token_answers_normally(string path)
    {
        using var response = await SendAsync(HttpMethod.Post, path, null);
        ((int)response.StatusCode).Should().BeInRange(200, 204);
        (await response.Content.ReadAsStringAsync()).Should().Be("");
    }

    [Theory]
    [InlineData("/api/jwtonly-logout")]
    [InlineData("/api/jwtonly-logout-named")]
    public async Task Logout_with_a_token_that_is_not_valid_answers_normally(string path)
    {
        using var response = await SendAsync(HttpMethod.Post, path, "not.a.token");
        ((int)response.StatusCode).Should().BeInRange(200, 204);
        (await response.Content.ReadAsStringAsync()).Should().Be("");
    }

    [Theory]
    [InlineData("/api/jwtonly-logout")]
    [InlineData("/api/jwtonly-logout-named")]
    public async Task Logout_with_a_valid_token_answers_normally_and_the_token_stays_valid(string path)
    {
        var token = Token();
        using var response = await SendAsync(HttpMethod.Post, path, token);
        ((int)response.StatusCode).Should().BeInRange(200, 204);
        // nothing is kept on the server for a JWT: the client discards it
        using var after = await SendAsync(HttpMethod.Get, "/api/jwtonly-ping", token);
        after.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
