using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NpgsqlRest.Mcp;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("McpAudienceFixture")]
public class McpAudienceFixtureCollection : ICollectionFixture<McpAudienceTestFixture> { }

/// <summary>
/// Fixture for MCP token audience binding (RFC 8707). The server is configured with a canonical
/// <c>Audience</c>; the "/login-as" endpoint signs the caller in with a single <c>aud</c> claim taken
/// from the query string, so tests can present a token whose audience matches or differs.
/// </summary>
public class McpAudienceTestFixture : IDisposable
{
    public const string Audience = "https://mcp.test/resource";

    private readonly WebApplication _app;

    public string ServerAddress { get; }

    public McpAudienceTestFixture()
    {
        Database.Create();
        var connectionString = Database.CreateAdditional("mcp_audience_test");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication().AddCookie();
        _app = builder.Build();

        _app.MapGet("/login-as", (string aud) => Results.SignIn(new ClaimsPrincipal(new ClaimsIdentity(
            claims: [new Claim("aud", aud)],
            authenticationType: CookieAuthenticationDefaults.AuthenticationScheme))));

        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            IncludeSchemas = ["mcp"],
            CommentsMode = CommentsMode.OnlyAnnotated,
            EndpointCreateHandlers =
            [
                new Mcp(new McpOptions
                {
                    Enabled = true,
                    Authorization = new McpAuthorizationOptions
                    {
                        RequireAuthorization = true,
                        AuthorizationServers = ["https://as.example.com"],
                        Audience = Audience,
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
