using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.RateLimiting;
using NpgsqlRest.Auth;
using NpgsqlRest.SqlFileSource;
using NpgsqlRest.TsClient;
using NpgsqlRestTests.SqlFileSourceTests;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("SqlFileSourceFixture")]
public class SqlFileSourceFixtureCollection : ICollectionFixture<SqlFileSourceTestFixture> { }

/// <summary>
/// Test fixture for SqlFileSource endpoint tests.
/// Creates SQL files in a temp directory and starts a web application
/// with SqlFileSource configured to scan them.
/// </summary>
public class SqlFileSourceTestFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly string _sqlDir;
    private readonly string _tsClientDir;

    public HttpClient Client => _client;
    public WebApplication App => _app;
    public string SqlDir => _sqlDir;
    public string TsClientDir => _tsClientDir;

    /// <summary>
    /// Create a fresh HttpClient with its own cookie container (for auth test isolation).
    /// </summary>
    public HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new System.Net.CookieContainer() };
        return new HttpClient(handler) { BaseAddress = _client.BaseAddress, Timeout = TimeSpan.FromHours(1) };
    }

    public SqlFileSourceTestFixture()
    {
        var connectionString = Database.Create();

        // Create temp directory for SQL files
        _sqlDir = Path.Combine(Path.GetTempPath(), "npgsqlrest_sql_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_sqlDir);
        var subDir = Path.Combine(_sqlDir, "sub");
        Directory.CreateDirectory(subDir);

        // Write SQL files for testing
        SqlFiles.WriteAll(_sqlDir, subDir);

        // TsClient output directory
        _tsClientDir = Path.Combine(Path.GetTempPath(), "npgsqlrest_tsclient_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tsClientDir);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddAuthentication().AddCookie();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (ctx, ct) =>
            {
                await ctx.HttpContext.Response.WriteAsync("Rate limit exceeded. Please try again later.", ct);
            };
            options.AddFixedWindowLimiter("max 2 per second", config =>
            {
                config.PermitLimit = 2;
                config.Window = TimeSpan.FromSeconds(1);
                config.AutoReplenishment = true;
            });
        });
        builder.Services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                ctx.ProblemDetails.Type = null;
                ctx.ProblemDetails.Extensions.Remove("traceId");
            };
        });

        _app = builder.Build();

        var authOptions = new NpgsqlRestAuthenticationOptions
        {
            DefaultUserIdClaimType = "name_identifier",
            DefaultNameClaimType = "name",
            DefaultRoleClaimType = "role",
            BasicAuth = new NpgsqlRest.Auth.BasicAuthOptions
            {
                SslRequirement = SslRequirement.Ignore
            },
            ContextKeyClaimsMapping = new()
            {
                { "request.user_id", "name_identifier" },
                { "request.user_name", "name" },
                { "request.user_roles", "role" },
            },
            ParameterNameClaimsMapping = new()
            {
                { "_user_id", "name_identifier" },
                { "_user_name", "name" },
                { "_user_roles", "role" },
            },
        };

        // Login endpoint for auth tests
        _app.MapGet("/login", () => Results.SignIn(new ClaimsPrincipal(new ClaimsIdentity(
            claims:
            [
                new Claim(authOptions.DefaultUserIdClaimType, "user123"),
                new Claim(authOptions.DefaultNameClaimType, "user"),
                new Claim(authOptions.DefaultRoleClaimType, "role1"),
                new Claim(authOptions.DefaultRoleClaimType, "role2"),
                new Claim(authOptions.DefaultRoleClaimType, "role3"),
            ],
            authenticationType: CookieAuthenticationDefaults.AuthenticationScheme))));

        _app.UseRateLimiter();

        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            CommentsMode = CommentsMode.ParseAll,
            AuthenticationOptions = authOptions,
            EndpointSources =
            [
                new SqlFileSource(new SqlFileSourceOptions
                {
                    FilePattern = _sqlDir.Replace('\\', '/') + "/**/*.sql",
                    CommentsMode = CommentsMode.ParseAll,
                    CommentScope = CommentScope.All,
                    ErrorMode = ParseErrorMode.Skip,
                })
            ],
            EndpointCreateHandlers =
            [
                new TsClient(new TsClientOptions
                {
                    FilePath = Path.Combine(_tsClientDir, "{0}.ts"),
                    BySchema = true,
                    IncludeStatusCode = false,
                })
            ],
        });

        _app.StartAsync().GetAwaiter().GetResult();

        var serverAddress = _app.Urls.First();
        _client = new HttpClient { BaseAddress = new Uri(serverAddress) };
        _client.Timeout = TimeSpan.FromHours(1);
    }

#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
    public void Dispose()
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
    {
        _client.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();

        // Clean up temp directories
        try { if (Directory.Exists(_sqlDir)) Directory.Delete(_sqlDir, true); } catch { }
        try { if (Directory.Exists(_tsClientDir)) Directory.Delete(_tsClientDir, true); } catch { }
    }
}
