using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// Setup for CacheKeyClaimMappedParamTest.
    ///
    /// Creates a function that takes `_user_id text` and returns "<user_id>:<random uuid>".
    /// The endpoint uses:
    ///   - `user_parameters` → enables auto-population of `_user_id` from the `name_identifier` claim
    ///     (per fixture's `ParameterNameClaimsMapping` mapping `_user_id` → `name_identifier`).
    ///   - `cached _user_id` → caches the response, keyed by the resolved `_user_id` value.
    ///   - `authorize` → requires authenticated user.
    ///
    /// `gen_random_uuid()` makes each fresh execution distinct. So:
    ///   - cache hit → both calls return the same response (same UUID).
    ///   - cache miss → second call returns a different UUID.
    /// We use this to detect both whether the cache works AND whether two different users get
    /// separate cache entries (which only happens if the resolved claim value is part of the key).
    /// </summary>
    public static void CacheKeyClaimMappedParamTest()
    {
        script.Append(@"
        create function cct_get_cached_per_user(_user_id text)
        returns text language plpgsql as $$
        begin
            return _user_id || ':' || gen_random_uuid()::text;
        end;
        $$;
        comment on function cct_get_cached_per_user(text) is '
        HTTP GET
        authorize
        user_parameters
        cached _user_id
        ';
        ");
    }
}

[Collection("CacheClaimTestFixture")]
public class CacheKeyClaimMappedParamTest(CacheClaimTestFixture test)
{
    /// <summary>
    /// Regression test: when a parameter is auto-populated from a claim (via
    /// `ParameterNameClaimsMapping` + `user_parameters` annotation), the cache key must use the
    /// resolved post-claim value — NOT the request-time null. Otherwise two different users
    /// would share a single cache entry, leaking responses across identities.
    ///
    /// Verifies four things in one test:
    ///   1. user_a's first call populates the cache and returns a fresh response.
    ///   2. user_a's second call hits the cache (identical response — same UUID).
    ///   3. user_b's first call does NOT hit user_a's entry (different starts: "user_b:" vs "user_a:")
    ///      and is itself cached.
    ///   4. user_a's third call still hits user_a's original entry (separate from user_b's).
    ///
    /// If step 3 returns user_a's value, the cache key was built before claim resolution and the
    /// cache is leaking across identities — a security bug. Test catches this.
    /// </summary>
    [Fact]
    public async Task Cache_key_uses_claim_resolved_user_id_so_different_users_get_separate_entries()
    {
        using var clientA = test.CreateClient();
        using var clientB = test.CreateClient();

        // user_a logs in
        using var loginA = await clientA.GetAsync("/cct-login-a");
        loginA.StatusCode.Should().Be(HttpStatusCode.OK);

        // user_b logs in (separate cookie container — independent identity)
        using var loginB = await clientB.GetAsync("/cct-login-b");
        loginB.StatusCode.Should().Be(HttpStatusCode.OK);

        // user_a — first call, populates cache
        using var responseA1 = await clientA.GetAsync("/api/cct-get-cached-per-user");
        responseA1.StatusCode.Should().Be(HttpStatusCode.OK);
        var bodyA1 = await responseA1.Content.ReadAsStringAsync();
        bodyA1.Should().StartWith("user_a:");

        // user_a — second call, must hit cache (same exact response, including UUID)
        using var responseA2 = await clientA.GetAsync("/api/cct-get-cached-per-user");
        responseA2.StatusCode.Should().Be(HttpStatusCode.OK);
        var bodyA2 = await responseA2.Content.ReadAsStringAsync();
        bodyA2.Should().Be(bodyA1);

        // user_b — first call. If the cache key was built without the claim value (i.e. with null),
        // user_b would hit user_a's cached entry and we'd see "user_a:..." here. We assert otherwise.
        using var responseB1 = await clientB.GetAsync("/api/cct-get-cached-per-user");
        responseB1.StatusCode.Should().Be(HttpStatusCode.OK);
        var bodyB1 = await responseB1.Content.ReadAsStringAsync();
        bodyB1.Should().StartWith("user_b:");
        bodyB1.Should().NotBe(bodyA1);

        // user_b — second call hits user_b's own cache entry
        using var responseB2 = await clientB.GetAsync("/api/cct-get-cached-per-user");
        responseB2.StatusCode.Should().Be(HttpStatusCode.OK);
        var bodyB2 = await responseB2.Content.ReadAsStringAsync();
        bodyB2.Should().Be(bodyB1);

        // user_a — third call still returns the original cached value, separate from user_b's
        using var responseA3 = await clientA.GetAsync("/api/cct-get-cached-per-user");
        responseA3.StatusCode.Should().Be(HttpStatusCode.OK);
        var bodyA3 = await responseA3.Content.ReadAsStringAsync();
        bodyA3.Should().Be(bodyA1);
    }
}
