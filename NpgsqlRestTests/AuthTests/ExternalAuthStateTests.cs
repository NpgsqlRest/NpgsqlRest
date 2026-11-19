using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NpgsqlRestClient;

namespace NpgsqlRestTests.AuthTests;

/// <summary>
/// Security regression (login CSRF): the external-login code POST must carry the OAuth state this browser was given
/// when the flow started (server-side, in an HttpOnly cookie). The state used to be checked only in browser script,
/// so another site could post an attacker's own authorization code and sign the victim into the attacker's account.
/// The provider's token URL points to an unreachable local port: a POST that passes the state check fails there (500)
/// instead of being rejected (400) - no real provider is contacted.
/// </summary>
public class ExternalAuthStateTests : IAsyncLifetime
{
    private const string Path = "/signin-google";
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var tempFile = System.IO.Path.GetTempFileName();
        File.WriteAllText(tempFile, """
        {
          "Auth": {
            "External": {
              "Enabled": true,
              "Google": { "Enabled": true, "ClientId": "test-client", "ClientSecret": "test-secret", "TokenUrl": "http://127.0.0.1:9/token" }
            }
          }
        }
        """);
        var config = new Config();
        try { config.Build([tempFile], []); } finally { File.Delete(tempFile); }
        var externalConfig = new ExternalAuthConfig();
        externalConfig.Build(config.Cfg.GetSection("Auth"), config, new Builder(config));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _app = builder.Build();
        _ = new ExternalAuth(externalConfig, "Host=127.0.0.1;Port=9", _app, new NpgsqlRestOptions("Host=127.0.0.1;Port=9") { ConnectionRetryOptions = new() { Enabled = false } },
            retryStrategy: null, PostgresConnectionNoticeLoggingMode.MessageOnly);
        await _app.StartAsync();
        _client = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(_app.Urls.First())
        };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task<string> StartFlow()
    {
        using var response = await _client.GetAsync(Path);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__npgsqlrest_oauth_state="));
        cookie.Should().Contain("httponly").And.Contain("samesite=lax").And.Contain($"path={Path}");
        var state = cookie["__npgsqlrest_oauth_state=".Length..cookie.IndexOf(';')];
        (await response.Content.ReadAsStringAsync()).Should().Contain($"var stateValue = \"{state}\";");
        return state;
    }

    private async Task<HttpResponseMessage> PostCode(string? state, string? cookieState)
    {
        var body = state is null ? "{\"code\":\"attacker-code\"}" : $"{{\"code\":\"attacker-code\",\"state\":\"{state}\"}}";
        var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (cookieState is not null)
        {
            request.Headers.Add("Cookie", $"__npgsqlrest_oauth_state={cookieState}");
        }
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Provider_callback_does_not_replace_the_flow_state()
    {
        await StartFlow();
        using var callback = await _client.GetAsync($"{Path}?code=abc&state=xyz");
        callback.StatusCode.Should().Be(HttpStatusCode.OK);
        callback.Headers.TryGetValues("Set-Cookie", out var cookies);
        (cookies ?? []).Should().NotContain(c => c.StartsWith("__npgsqlrest_oauth_state="));
    }

    [Fact]
    public async Task Code_post_without_state_cookie_is_rejected()
    {
        using var response = await PostCode(state: "anything", cookieState: null);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Be("Invalid or expired sign-in state. Please start the sign-in again.");
    }

    [Fact]
    public async Task Code_post_with_mismatched_state_is_rejected()
    {
        var state = await StartFlow();
        using var response = await PostCode(state: "not-" + state, cookieState: state);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Code_post_without_state_in_body_is_rejected()
    {
        var state = await StartFlow();
        using var response = await PostCode(state: null, cookieState: state);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Code_post_with_matching_state_passes_the_check()
    {
        var state = await StartFlow();
        using var response = await PostCode(state: state, cookieState: state);
        // past the state check: the (unreachable) token endpoint fails the exchange
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().Be("Error talking to Google");
    }
}
