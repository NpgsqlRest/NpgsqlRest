using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// Setup for When_ParamNotInCacheKey_RuleDropped_Test.
    /// Function takes two params (`a`, `b`). The bespoke fixture for this test will configure a profile
    /// with `Parameters: ["a"]` (only `a` in the cache key) but a When rule referencing `b` (NOT in the
    /// cache key). At startup the rule must be dropped with a Warning, and at runtime the rule has no effect.
    /// </summary>
    public static void When_ParamNotInCacheKey_RuleDropped_Test()
    {
        script.Append(@"
        create function cpx_when_invalid_param(a text, b text default null)
        returns text language plpgsql as $$
        begin
            return a || ':' || coalesce(b, 'null') || ':' || gen_random_uuid()::text;
        end;
        $$;
        comment on function cpx_when_invalid_param(text, text) is '
        HTTP GET
        cache_profile invalid_param_test
        ';
        ");
    }
}

public class When_ParamNotInCacheKey_RuleDropped_Test : IDisposable
{
    private readonly WebApplication _app;
    private readonly string _serverAddress;

    public When_ParamNotInCacheKey_RuleDropped_Test()
    {
        var connectionString = Database.Create();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();

        _app.UseNpgsqlRest(new(connectionString)
        {
            IncludeSchemas = ["public"],
            NameSimilarTo = "cpx_when_invalid_param",
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false,
            CacheOptions = new()
            {
                DefaultRoutineCache = new RoutineCache(),
                Profiles = new()
                {
                    ["invalid_param_test"] = new CacheProfile
                    {
                        Cache = new RoutineCache(),
                        // Cache key uses only `a` — `b` is intentionally NOT in the key.
                        Parameters = ["a"],
                        // This rule references `b` which is NOT in the cache key. Builder must drop it
                        // with a Warning so different `b` values cannot share the same cache entry yet
                        // produce different rule outcomes (a confusing scenario).
                        When =
                        [
                            new CacheWhenRule { Parameter = "b", Value = null, Skip = true }
                        ]
                    }
                }
            }
        });

        _app.StartAsync().GetAwaiter().GetResult();
        _serverAddress = _app.Urls.First();
    }

    /// <summary>
    /// The rule referencing `b` (not in the cache key) must be dropped at startup. The endpoint should still
    /// register and cache normally based on `a`. We prove the drop by sending a request that WOULD trigger
    /// the rule if it were active (b=null) and verifying the response is cached anyway.
    /// </summary>
    [Fact]
    public async Task Rule_referencing_param_not_in_cache_key_is_dropped_at_startup_and_has_no_runtime_effect()
    {
        using var client = new HttpClient { BaseAddress = new Uri(_serverAddress), Timeout = TimeSpan.FromMinutes(5) };

        // Both calls send a=x and omit b (so b is null at runtime). If the rule had survived, both calls
        // would bypass the cache (different UUIDs). Since the rule was dropped, the cache works normally
        // and the second call must hit cache.
        using var r1 = await client.GetAsync("/api/cpx-when-invalid-param/?a=x");
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        var b1 = await r1.Content.ReadAsStringAsync();
        b1.Should().StartWith("x:");

        using var r2 = await client.GetAsync("/api/cpx-when-invalid-param/?a=x");
        var b2 = await r2.Content.ReadAsStringAsync();
        b2.Should().Be(b1, "rule referencing 'b' (not in cache key) was dropped at startup → cache works normally on 'a' alone → second call hits cache");
    }

#pragma warning disable CA1816
    public void Dispose()
#pragma warning restore CA1816
    {
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
    }
}
