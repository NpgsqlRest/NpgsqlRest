using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NpgsqlRest.Mcp;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("McpAuthRoleFixture")]
public class McpAuthRoleFixtureCollection : ICollectionFixture<McpAuthRoleTestFixture> { }

/// <summary>
/// Fixture for MCP Layer-2 authorization (per-tool roles). Cookie auth is wired with a "/login-as"
/// endpoint that signs in a principal carrying a single role claim. The MCP server runs with
/// RequireAuthorization=false (anonymous discovery allowed) but the <c>tool_authorized</c> tool carries
/// <c>@authorize admin</c>, so a wrong-role caller is rejected on tools/call with 403 insufficient_scope.
/// </summary>
public class McpAuthRoleTestFixture : IDisposable
{
    private readonly WebApplication _app;

    public string ServerAddress { get; }

    public McpAuthRoleTestFixture()
    {
        Database.Create();
        var connectionString = Database.CreateAdditional("mcp_auth_role_test");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication().AddCookie();
        _app = builder.Build();

        // Sign the caller in with a single "role" claim taken from the query string.
        _app.MapGet("/login-as", (string role) => Results.SignIn(new ClaimsPrincipal(new ClaimsIdentity(
            claims: [new Claim("role", role)],
            authenticationType: CookieAuthenticationDefaults.AuthenticationScheme))));

        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            IncludeSchemas = ["mcp"],
            CommentsMode = CommentsMode.OnlyAnnotated,
            AuthenticationOptions = new() { DefaultRoleClaimType = "role" },
            EndpointCreateHandlers =
            [
                new Mcp(new McpOptions
                {
                    Enabled = true,
                    Authorization = new McpAuthorizationOptions
                    {
                        // Gate off — anonymous discovery is allowed; per-tool `authorize` still applies.
                        RequireAuthorization = false,
                        AuthorizationServers = ["https://as.example.com"],
                        ScopesSupported = ["mcp.read"],
                    }
                })
            ]
        });

        _app.StartAsync().GetAwaiter().GetResult();
        ServerAddress = _app.Urls.First();
    }

    public HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new System.Net.CookieContainer() };
        return new HttpClient(handler) { BaseAddress = new Uri(ServerAddress), Timeout = TimeSpan.FromMinutes(5) };
    }

#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
    public void Dispose()
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
    {
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
    }
}
