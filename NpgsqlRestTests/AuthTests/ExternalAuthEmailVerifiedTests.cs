using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NpgsqlRestClient;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace NpgsqlRestTests.AuthTests;

/// <summary>
/// Security regression: an external login is rejected when the provider itself reports the account's email as not
/// verified. The login command identifies users by that email, so an account registered with someone else's address
/// could sign in as that person. A fake provider (WireMock) serves the token and user info endpoints; a login that
/// passes the check fails afterwards at the (unreachable) database instead (500).
/// </summary>
public class ExternalAuthEmailVerifiedTests : IAsyncLifetime
{
    private const string Path = "/signin-google";
    private WireMockServer _provider = null!;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _provider = WireMockServer.Start();
        _provider
            .Given(Request.Create().WithPath("/token").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("{\"access_token\":\"provider-token\"}"));

        var tempFile = System.IO.Path.GetTempFileName();
        File.WriteAllText(tempFile, $$"""
        {
          "Auth": {
            "External": {
              "Enabled": true,
              "Google": { "Enabled": true, "ClientId": "test-client", "ClientSecret": "test-secret",
                "TokenUrl": "{{_provider.Url}}/token", "InfoUrl": "{{_provider.Url}}/userinfo" }
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
        _provider.Stop();
    }

    private async Task<HttpResponseMessage> SignInAsync(string userInfoJson)
    {
        _provider
            .Given(Request.Create().WithPath("/userinfo").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(userInfoJson));

        using var start = await _client.GetAsync(Path);
        var cookie = start.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__npgsqlrest_oauth_state="));
        var state = cookie["__npgsqlrest_oauth_state=".Length..cookie.IndexOf(';')];

        var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent($"{{\"code\":\"provider-code\",\"state\":\"{state}\"}}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Cookie", $"__npgsqlrest_oauth_state={state}");
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Unverified_email_is_rejected()
    {
        using var response = await SignInAsync("{\"sub\":\"1\",\"email\":\"victim@example.com\",\"email_verified\":false}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Be(
            "The email address of this Google account is not verified. Verify it with Google and sign in again.");
    }

    [Fact]
    public async Task Unverified_email_as_string_is_rejected()
    {
        using var response = await SignInAsync("{\"sub\":\"1\",\"email\":\"victim@example.com\",\"email_verified\":\"false\"}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Verified_email_passes_the_check()
    {
        using var response = await SignInAsync("{\"sub\":\"1\",\"email\":\"user@example.com\",\"email_verified\":true}");
        // past the check: the login command fails at the unreachable test database
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().Be("Error talking to Google");
    }

    [Theory]
    [InlineData("{\"email\":\"a@b.c\"}", false)] // no flag (e.g. GitHub, Facebook): not treated as unverified
    [InlineData("{\"email\":\"a@b.c\",\"email_verified\":true}", false)]
    [InlineData("{\"email\":\"a@b.c\",\"email_verified\":\"true\"}", false)]
    [InlineData("{\"email\":\"a@b.c\",\"email_verified\":false}", true)]
    [InlineData("{\"email\":\"a@b.c\",\"email_verified\":\"False\"}", true)]
    [InlineData("{\"email\":\"a@b.c\",\"verified_email\":false}", true)]
    public void Unverified_flag_detection(string json, bool expected)
    {
        ExternalAuth.IsEmailExplicitlyUnverified(JsonNode.Parse(json)).Should().Be(expected);
    }
}
