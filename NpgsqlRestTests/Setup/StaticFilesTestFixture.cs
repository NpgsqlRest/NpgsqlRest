using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NpgsqlRest.Auth;
using NpgsqlRestClient;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("StaticFilesTestFixture")]
public class StaticFilesTestFixtureCollection : ICollectionFixture<StaticFilesTestFixture> { }

/// <summary>
/// Test fixture for StaticFiles middleware tests.
/// Creates a web application with StaticFiles enabled to verify:
/// - AuthorizePaths pattern matching and redirect behavior
/// - Content parsing with claims replacement
/// - Antiforgery token injection
/// - Cache headers for parsed content
/// </summary>
public class StaticFilesTestFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly HttpClient _authenticatedClient;
    private readonly string _webRootPath;

    public HttpClient Client => _client;
    public HttpClient AuthenticatedClient => _authenticatedClient;
    public string ServerAddress { get; }

    /// <summary>
    /// Test claim values for authenticated user
    /// </summary>
    public const string TestUserId = "test-user-123";
    public const string TestUserName = "TestUser";
    public const string TestUserRole = "admin";

    public StaticFilesTestFixture()
    {
        var connectionString = Database.Create();

        // Create temp directory for static files
        _webRootPath = Path.Combine(Path.GetTempPath(), "NpgsqlRestTests", "StaticFilesTestStaticFiles", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_webRootPath);
        Directory.CreateDirectory(Path.Combine(_webRootPath, "protected"));
        Directory.CreateDirectory(Path.Combine(_webRootPath, "public"));

        // Create test static files
        CreateTestStaticFiles();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            WebRootPath = _webRootPath
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // Add cookie authentication for testing authorization
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/login";
                options.Cookie.Name = "TestAuth";
            });
        builder.Services.AddAuthorization();

        // Add antiforgery for testing token injection
        builder.Services.AddAntiforgery(options =>
        {
            options.FormFieldName = "__TestAntiforgeryToken";
            options.HeaderName = "X-Test-Antiforgery";
        });

        _app = builder.Build();

        _app.UseAuthentication();
        _app.UseAuthorization();

        // Configure static file middleware with authorization and parsing
        _app.UseDefaultFiles();

        var authOptions = new NpgsqlRestAuthenticationOptions
        {
            DefaultUserIdClaimType = "user_id",
            DefaultNameClaimType = "user_name",
            DefaultRoleClaimType = "user_roles"
        };

        AppStaticFileMiddleware.ConfigureStaticFileMiddleware(
            parse: true,
            parsePatterns: ["*.html"],
            options: authOptions,
            cacheParsedFiles: false, // Disable cache for testing
            antiforgeryFieldNameTag: "antiForgeryFieldName",
            antiforgeryTokenTag: "antiForgeryToken",
            antiforgery: _app.Services.GetService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>(),
            headers: ["Cache-Control: no-store, no-cache, must-revalidate", "Pragma: no-cache"],
            authorizePaths: ["/protected/*", "*.secret.html"],
            unauthorizedRedirectPath: "/login.html",
            unautorizedReturnToQueryParameter: "return_to",
            availableClaimTypes: ["user_id", "user_name", "user_roles"],
            logger: null);

        _app.UseMiddleware<AppStaticFileMiddleware>();

        // Add a simple login endpoint for testing
        _app.MapGet("/test-login", async (HttpContext context) =>
        {
            var claims = new List<Claim>
            {
                new("user_id", TestUserId),
                new("user_name", TestUserName),
                new("user_roles", TestUserRole)
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            context.Response.StatusCode = 200;
            await context.Response.WriteAsync("Logged in");
        });

        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            IncludeSchemas = ["public"],
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false
        });

        _app.StartAsync().GetAwaiter().GetResult();

        ServerAddress = _app.Urls.First();

        // Create unauthenticated client
        _client = new HttpClient { BaseAddress = new Uri(ServerAddress) };
        _client.Timeout = TimeSpan.FromMinutes(5);

        // Create authenticated client with cookies
        var handler = new HttpClientHandler
        {
            CookieContainer = new System.Net.CookieContainer(),
            UseCookies = true
        };
        _authenticatedClient = new HttpClient(handler) { BaseAddress = new Uri(ServerAddress) };
        _authenticatedClient.Timeout = TimeSpan.FromMinutes(5);

        // Authenticate the client
        AuthenticateClient().GetAwaiter().GetResult();
    }

    private async Task AuthenticateClient()
    {
        // Call the test login endpoint to get authentication cookie
        var response = await _authenticatedClient.GetAsync("/test-login");
        response.EnsureSuccessStatusCode();
    }

    private void CreateTestStaticFiles()
    {
        // Public HTML file with claim placeholders
        var publicHtml = """
            <!DOCTYPE html>
            <html>
            <head><title>Public Page</title></head>
            <body>
                <h1>Public Page</h1>
                <p>User ID: {user_id}</p>
                <p>User Name: {user_name}</p>
                <p>User Roles: {user_roles}</p>
                <form>
                    <input type="hidden" name="{antiForgeryFieldName}" value="{antiForgeryToken}">
                </form>
            </body>
            </html>
            """;
        File.WriteAllText(Path.Combine(_webRootPath, "public", "index.html"), publicHtml);

        // Protected HTML file
        var protectedHtml = """
            <!DOCTYPE html>
            <html>
            <head><title>Protected Page</title></head>
            <body>
                <h1>Protected Content</h1>
                <p>Welcome, {user_name}!</p>
                <p>Your ID: {user_id}</p>
            </body>
            </html>
            """;
        File.WriteAllText(Path.Combine(_webRootPath, "protected", "secret.html"), protectedHtml);

        // Secret file matching pattern *.secret.html
        var secretHtml = """
            <!DOCTYPE html>
            <html>
            <head><title>Secret Page</title></head>
            <body>
                <h1>Top Secret!</h1>
            </body>
            </html>
            """;
        File.WriteAllText(Path.Combine(_webRootPath, "data.secret.html"), secretHtml);

        // Login page
        var loginHtml = """
            <!DOCTYPE html>
            <html>
            <head><title>Login</title></head>
            <body>
                <h1>Please Log In</h1>
            </body>
            </html>
            """;
        File.WriteAllText(Path.Combine(_webRootPath, "login.html"), loginHtml);

        // Non-parsed file (not .html)
        var jsContent = "console.log('test');";
        File.WriteAllText(Path.Combine(_webRootPath, "public", "script.js"), jsContent);
    }

#pragma warning disable CA1816
    public void Dispose()
#pragma warning restore CA1816
    {
        _client.Dispose();
        _authenticatedClient.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();

        try
        {
            if (Directory.Exists(_webRootPath))
            {
                Directory.Delete(_webRootPath, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
