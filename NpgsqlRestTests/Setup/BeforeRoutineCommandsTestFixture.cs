using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NpgsqlRest.Auth;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("BeforeRoutineCommandsTestFixture")]
public class BeforeRoutineCommandsTestFixtureCollection : ICollectionFixture<BeforeRoutineCommandsTestFixture> { }

/// <summary>
/// Test fixture for BeforeRoutineCommands and WrapInTransaction options.
/// Configured with:
/// - WrapInTransaction = true (so set_config calls use is_local=true and run inside an explicit transaction)
/// - BeforeRoutineCommands covering all three parameter sources (Claim, RequestHeader, IpAddress) and the shorthand-string form
/// </summary>
public class BeforeRoutineCommandsTestFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public HttpClient Client => _client;
    public string ServerAddress { get; }

    public BeforeRoutineCommandsTestFixture()
    {
        var connectionString = Database.Create();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddAuthentication().AddCookie();

        _app = builder.Build();

        // Login endpoint that sets a tenant_id claim and a name claim
        _app.MapGet("/brc-login", () => Results.SignIn(new ClaimsPrincipal(new ClaimsIdentity(
            claims: new[]
            {
                new Claim("name_identifier", "42"),
                new Claim("name", "tenant_user"),
                new Claim("tenant_id", "tenant_a"),
            },
            authenticationType: CookieAuthenticationDefaults.AuthenticationScheme))));

        _app.UseNpgsqlRest(new(connectionString)
        {
            IncludeSchemas = ["public"],
            NameSimilarTo = "brc_%",
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false,
            WrapInTransaction = true,
            AuthenticationOptions = new()
            {
                DefaultUserIdClaimType = "name_identifier",
                DefaultNameClaimType = "name",
                DefaultRoleClaimType = "role",
            },
            BeforeRoutineCommands = [
                // Shorthand: raw SQL, no parameters. clock_timestamp() resolves at PG side.
                "select set_config('app.request_time', clock_timestamp()::text, true)",

                // Claim source — tenant_id from the login claim.
                new BeforeRoutineCommand
                {
                    Sql = "select set_config('app.tenant_id', $1, true)",
                    Parameters = [new BeforeRoutineCommandParameter
                    {
                        Source = BeforeRoutineCommandParameterSource.Claim,
                        Name = "tenant_id"
                    }]
                },

                // Sets search_path to the value of the tenant_id GUC set above (chained).
                "select set_config('search_path', case when current_setting('app.tenant_id', true) <> '' then current_setting('app.tenant_id', true) || ', public' else 'public' end, true)",

                // RequestHeader source — User-Agent.
                new BeforeRoutineCommand
                {
                    Sql = "select set_config('app.user_agent', $1, true)",
                    Parameters = [new BeforeRoutineCommandParameter
                    {
                        Source = BeforeRoutineCommandParameterSource.RequestHeader,
                        Name = "User-Agent"
                    }]
                },

                // IpAddress source.
                new BeforeRoutineCommand
                {
                    Sql = "select set_config('app.client_ip', $1, true)",
                    Parameters = [new BeforeRoutineCommandParameter
                    {
                        Source = BeforeRoutineCommandParameterSource.IpAddress
                    }]
                },

                // Demonstrates ordering: stage1 set, then stage2 set from stage1.
                "select set_config('app.stage1', 'one', true)",
                "select set_config('app.stage2', current_setting('app.stage1', true) || '-two', true)",
            ]
        });

        _app.StartAsync().GetAwaiter().GetResult();

        ServerAddress = _app.Urls.First();

        _client = new HttpClient { BaseAddress = new Uri(ServerAddress) };
        _client.Timeout = TimeSpan.FromMinutes(5);
    }

#pragma warning disable CA1816
    public void Dispose()
#pragma warning restore CA1816
    {
        _client.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
    }
}
