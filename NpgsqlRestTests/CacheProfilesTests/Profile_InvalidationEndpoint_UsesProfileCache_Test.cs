using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// Setup for Profile_InvalidationEndpoint_UsesProfileCache_Test.
    /// Function annotated with `cache_profile inv_test` and `cached`. The fixture for this test
    /// configures one profile ("inv_test") with an in-memory cache and an `InvalidateCacheSuffix`,
    /// so each cached endpoint also gets a /invalidate sibling.
    ///
    /// Goal: prove the invalidation endpoint removes from the *profile's* cache, not the root cache.
    /// </summary>
    public static void Profile_InvalidationEndpoint_UsesProfileCache_Test()
    {
        script.Append(@"
        create function cpx_invalidation_routes_to_profile()
        returns text language plpgsql as $$
        begin
            return gen_random_uuid()::text;
        end;
        $$;
        comment on function cpx_invalidation_routes_to_profile() is '
        HTTP GET
        cache_profile inv_test
        ';
        ");
    }
}

public class Profile_InvalidationEndpoint_UsesProfileCache_Test : IDisposable
{
    private readonly WebApplication _app;
    private readonly string _serverAddress;

    public Profile_InvalidationEndpoint_UsesProfileCache_Test()
    {
        var connectionString = Database.Create();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();

        _app.UseNpgsqlRest(new(connectionString)
        {
            IncludeSchemas = ["public"],
            NameSimilarTo = "cpx_invalidation_routes_to_profile",
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false,
            CacheOptions = new()
            {
                DefaultRoutineCache = new RoutineCache(),
                InvalidateCacheSuffix = "invalidate",
                Profiles = new()
                {
                    ["inv_test"] = new CacheProfile { Cache = new RoutineCache() }
                }
            }
        });

        _app.StartAsync().GetAwaiter().GetResult();
        _serverAddress = _app.Urls.First();
    }

    /// <summary>
    /// Calling /invalidate on a profile-cached endpoint must remove the entry from that profile's
    /// cache (otherwise subsequent calls would still hit the stale entry). Sequence:
    ///   1. Call → cache write (UUID1).
    ///   2. Call again → cache hit (UUID1).
    ///   3. Call /invalidate → entry removed from profile cache.
    ///   4. Call → fresh execution (UUID2 ≠ UUID1).
    /// </summary>
    [Fact]
    public async Task Invalidation_endpoint_removes_entry_from_profile_cache()
    {
        using var client = new HttpClient { BaseAddress = new Uri(_serverAddress) };

        using var r1 = await client.GetAsync("/api/cpx-invalidation-routes-to-profile/");
        var body1 = await r1.Content.ReadAsStringAsync();

        using var r2 = await client.GetAsync("/api/cpx-invalidation-routes-to-profile/");
        var body2 = await r2.Content.ReadAsStringAsync();
        body2.Should().Be(body1, "second call should hit profile's cache");

        using var inv = await client.GetAsync("/api/cpx-invalidation-routes-to-profile/invalidate");
        inv.StatusCode.Should().Be(HttpStatusCode.OK);
        var invBody = await inv.Content.ReadAsStringAsync();
        invBody.Should().Be("{\"invalidated\":true}", "invalidation should report a removal from the profile's cache");

        using var r3 = await client.GetAsync("/api/cpx-invalidation-routes-to-profile/");
        var body3 = await r3.Content.ReadAsStringAsync();
        body3.Should().NotBe(body1, "after invalidate, third call should compute fresh — confirming the removal happened in the profile's cache, not somewhere else");
    }

#pragma warning disable CA1816
    public void Dispose()
#pragma warning restore CA1816
    {
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
    }
}
