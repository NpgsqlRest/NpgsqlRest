using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("CacheProfilesTestFixture")]
public class CacheProfilesTestFixtureCollection : ICollectionFixture<CacheProfilesTestFixture> { }

/// <summary>
/// Fixture for caching profile tests. Configures CacheOptions with multiple in-memory profiles, each backed by
/// its own <see cref="RoutineCache"/> instance so individual tests can inspect or reason about specific backends.
///
/// Profiles registered:
///   - "fast"       — Memory, 5min expiration, Parameters: ["key"]
///   - "slow"       — Memory, 1h expiration, Parameters: null (use all params)
///   - "url_only"   — Memory, no expiration, Parameters: [] (URL-only cache)
///   - "all_params" — Memory, no expiration, Parameters: null (use all params)
///   - "short_ttl"  — Memory, 1s expiration (for expiration-override tests)
///   - "skip_to"            — When: [{end_date null → skip}]; Parameters:["end_date"]
///   - "skip_to_or_format"  — When: [{end_date null → skip}, {format "csv" → skip}]; Parameters:["end_date","format"]
///   - "skip_status_array"  — When: [{status [null,""] → skip}]; Parameters:["status"]
///   - "tier_ttl"           — When: [{tier "free" → 5s TTL}, {tier "pro" → 1h TTL}]; Parameters:["tier"] (dynamic-TTL test)
///   - "first_match_wins"   — When: [{x "a" → skip}, {x "a" → 1h TTL}]; Parameters:["x"] (precedence: first rule wins)
///
/// Each profile uses a dedicated <see cref="RoutineCache"/> instance to make per-profile assertions easy.
/// </summary>
public class CacheProfilesTestFixture : IDisposable
{
    private readonly WebApplication _app;

    public string ServerAddress { get; }

    public CacheProfilesTestFixture()
    {
        var connectionString = Database.Create();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();

        _app.UseNpgsqlRest(new(connectionString)
        {
            IncludeSchemas = ["public"],
            // Match cp_* but exclude cpx_*. The brackets force `_` to be a literal underscore (not the
            // SQL-wildcard "any single character"); `[^x]` excludes any happy-path function from picking
            // up failure-path functions whose names start with cpx_.
            NameSimilarTo = "cp[_][^x]%",
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false,
            CacheOptions = new()
            {
                DefaultRoutineCache = new RoutineCache(),
                MemoryCachePruneIntervalSeconds = 3600,
                Profiles = new()
                {
                    ["fast"] = new CacheProfile
                    {
                        Cache = new RoutineCache(),
                        Expiration = TimeSpan.FromMinutes(5),
                        Parameters = ["key"]
                    },
                    ["slow"] = new CacheProfile
                    {
                        Cache = new RoutineCache(),
                        Expiration = TimeSpan.FromHours(1)
                    },
                    ["url_only"] = new CacheProfile
                    {
                        Cache = new RoutineCache(),
                        Parameters = []
                    },
                    ["all_params"] = new CacheProfile
                    {
                        Cache = new RoutineCache(),
                        Parameters = null
                    },
                    ["short_ttl"] = new CacheProfile
                    {
                        Cache = new RoutineCache(),
                        Expiration = TimeSpan.FromSeconds(1)
                    },
                    ["skip_to"] = new CacheProfile
                    {
                        Cache = new RoutineCache(),
                        Parameters = ["end_date"],
                        When =
                        [
                            new CacheWhenRule { Parameter = "end_date", Value = null, Skip = true }
                        ]
                    },
                    ["skip_to_or_format"] = new CacheProfile
                    {
                        Cache = new RoutineCache(),
                        Parameters = ["end_date", "format"],
                        When =
                        [
                            new CacheWhenRule { Parameter = "end_date", Value = null, Skip = true },
                            new CacheWhenRule { Parameter = "format", Value = JsonValue.Create("csv"), Skip = true }
                        ]
                    },
                    ["skip_status_array"] = new CacheProfile
                    {
                        Cache = new RoutineCache(),
                        Parameters = ["status"],
                        When =
                        [
                            new CacheWhenRule
                            {
                                Parameter = "status",
                                Value = new JsonArray((JsonNode?)null, (JsonNode?)JsonValue.Create("")),
                                Skip = true
                            }
                        ]
                    },
                    // Dynamic-TTL profile: different `tier` values yield different TTLs (no skip).
                    ["tier_ttl"] = new CacheProfile
                    {
                        Cache = new RoutineCache(),
                        Parameters = ["tier"],
                        When =
                        [
                            new CacheWhenRule { Parameter = "tier", Value = JsonValue.Create("free"), ThenExpiration = TimeSpan.FromSeconds(1) },
                            new CacheWhenRule { Parameter = "tier", Value = JsonValue.Create("pro"),  ThenExpiration = TimeSpan.FromHours(1) }
                        ]
                    },
                    // First-match-wins precedence: the first rule (Skip) should win over the second (TTL).
                    ["first_match_wins"] = new CacheProfile
                    {
                        Cache = new RoutineCache(),
                        Parameters = ["x"],
                        When =
                        [
                            new CacheWhenRule { Parameter = "x", Value = JsonValue.Create("a"), Skip = true },
                            new CacheWhenRule { Parameter = "x", Value = JsonValue.Create("a"), ThenExpiration = TimeSpan.FromHours(1) }
                        ]
                    }
                }
            }
        });

        _app.StartAsync().GetAwaiter().GetResult();
        ServerAddress = _app.Urls.First();
    }

    public HttpClient CreateClient()
        => new() { BaseAddress = new Uri(ServerAddress), Timeout = TimeSpan.FromMinutes(5) };

#pragma warning disable CA1816
    public void Dispose()
#pragma warning restore CA1816
    {
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
    }
}
