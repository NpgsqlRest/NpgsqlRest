using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Nodes;
using NpgsqlRest.Mcp;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("McpClaimFixture")]
public class McpClaimFixtureCollection : ICollectionFixture<McpClaimTestFixture> { }

/// <summary>
/// Confirms that a claim-mapped routine parameter binds from the forwarded principal on tools/call (the
/// point of forwarding the ClaimsPrincipal), and that such a parameter is hidden from inputSchema. The
/// isolated <c>mcp_claim</c> schema holds <c>claim_echo(_user_id)</c> mapped to the <c>name_identifier</c>
/// claim. "/login-as?uid=" signs the caller in with that claim.
/// </summary>
public class McpClaimTestFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly Mcp _mcp = new(new McpOptions { Enabled = true });

    public string ServerAddress { get; }
    public IReadOnlyDictionary<string, JsonObject> Tools => _mcp.Tools;

    public McpClaimTestFixture()
    {
        Database.Create();
        var connectionString = Database.CreateAdditional("mcp_claim_test");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication().AddCookie();
        _app = builder.Build();

        _app.MapGet("/login-as", (string uid) => Results.SignIn(new ClaimsPrincipal(new ClaimsIdentity(
            claims: [new Claim("name_identifier", uid)],
            authenticationType: CookieAuthenticationDefaults.AuthenticationScheme))));

        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            IncludeSchemas = ["mcp_claim"],
            CommentsMode = CommentsMode.OnlyAnnotated,
            AuthenticationOptions = new()
            {
                DefaultUserIdClaimType = "name_identifier",
                UseUserParameters = true,
                ParameterNameClaimsMapping = new() { { "_user_id", "name_identifier" } },
            },
            EndpointCreateHandlers = [_mcp],
        });

        _app.StartAsync().GetAwaiter().GetResult();
        ServerAddress = _app.Urls.First();
    }

    public HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new System.Net.CookieContainer() };
        return new HttpClient(handler) { BaseAddress = new Uri(ServerAddress), Timeout = TimeSpan.FromHours(1) };
    }

#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
    public void Dispose()
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
    {
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
    }
}
