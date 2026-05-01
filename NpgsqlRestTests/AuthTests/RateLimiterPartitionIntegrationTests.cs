using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// SQL setup for the rate-limiter-partition integration tests. Creates three trivial functions,
    /// each tied to one of the three policies registered by <see cref="RateLimiterPartitionTestFixture"/>:
    ///
    ///   - <c>rlpt_per_claim()</c>      → policy "rlpt-per-claim"      (per-claim partitioning)
    ///   - <c>rlpt_per_ip_fallback()</c> → policy "rlpt-per-ip-fallback" (claim → IP fallback)
    ///   - <c>rlpt_bypass_auth()</c>     → policy "rlpt-bypass-auth"     (auth users bypass)
    ///
    /// All three return the literal "ok" — the test doesn't care about the body, only the status code
    /// and whether enough calls succeed before getting throttled.
    /// </summary>
    public static void RateLimiterPartitionIntegrationTests()
    {
        script.Append("""

        create function rlpt_per_claim()
        returns text language sql as $$ select 'ok' $$;
        comment on function rlpt_per_claim() is '
        HTTP GET
        rate_limiter rlpt-per-claim
        ';

        create function rlpt_per_ip_fallback()
        returns text language sql as $$ select 'ok' $$;
        comment on function rlpt_per_ip_fallback() is '
        HTTP GET
        rate_limiter rlpt-per-ip-fallback
        ';

        create function rlpt_bypass_auth()
        returns text language sql as $$ select 'ok' $$;
        comment on function rlpt_bypass_auth() is '
        HTTP GET
        rate_limiter rlpt-bypass-auth
        ';
""");
    }
}

[Collection("RateLimiterPartitionTestFixture")]
public class RateLimiterPartitionIntegrationTests(RateLimiterPartitionTestFixture test)
{
    /// <summary>
    /// Per-claim partitioning: two signed-in users hit the same endpoint. Each has its own bucket,
    /// so user_a exhausting their quota MUST NOT cause user_b's next call to fail.
    ///
    /// Without partitioning, all five sequential calls would share one bucket; the third call from
    /// either client would 429. With partitioning by name_identifier, each client gets PermitLimit=2,
    /// so each can make exactly two successful calls.
    /// </summary>
    [Fact]
    public async Task Per_claim_partitioning_gives_each_user_its_own_bucket()
    {
        using var clientA = test.CreateClient();
        using var clientB = test.CreateClient();

        (await clientA.GetAsync("/rlpt-login-a")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await clientB.GetAsync("/rlpt-login-b")).StatusCode.Should().Be(HttpStatusCode.OK);

        // user_a: two allowed, third throttled.
        (await clientA.GetAsync("/api/rlpt-per-claim")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await clientA.GetAsync("/api/rlpt-per-claim")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await clientA.GetAsync("/api/rlpt-per-claim")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // user_b: still has their full quota — partitioning is working.
        (await clientB.GetAsync("/api/rlpt-per-claim")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await clientB.GetAsync("/api/rlpt-per-claim")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await clientB.GetAsync("/api/rlpt-per-claim")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// Source fall-through: when the first source (Claim) cannot resolve, partition resolution
    /// must fall through to the next source (IpAddress). Anonymous requests on this fixture all
    /// come from 127.0.0.1, so they share a single IP-keyed bucket — verifying fallback fires.
    /// </summary>
    [Fact]
    public async Task Falls_through_to_ip_when_claim_missing()
    {
        // Two anonymous clients (no login). Both will resolve to the loopback IP, so they share a bucket.
        using var anon1 = test.CreateClient();
        using var anon2 = test.CreateClient();

        (await anon1.GetAsync("/api/rlpt-per-ip-fallback")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anon2.GetAsync("/api/rlpt-per-ip-fallback")).StatusCode.Should().Be(HttpStatusCode.OK);
        // Bucket exhausted — same IP, regardless of which anonymous client makes the call.
        (await anon1.GetAsync("/api/rlpt-per-ip-fallback")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await anon2.GetAsync("/api/rlpt-per-ip-fallback")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// BypassAuthenticated: signed-in users get NoLimiter (unlimited), anonymous users still
    /// share a single bucket (Static "all-anon-share-bucket"). The auth user hammers the
    /// endpoint past PermitLimit and never sees a 429.
    /// </summary>
    [Fact]
    public async Task Authenticated_users_bypass_when_BypassAuthenticated_is_set()
    {
        using var authClient = test.CreateClient();
        using var anonClient = test.CreateClient();

        (await authClient.GetAsync("/rlpt-login-a")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Authenticated user makes 5 calls — well past the PermitLimit of 2 — and is never throttled.
        for (var i = 0; i < 5; i++)
        {
            (await authClient.GetAsync("/api/rlpt-bypass-auth")).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Anonymous user does fall into the rate-limited path.
        (await anonClient.GetAsync("/api/rlpt-bypass-auth")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonClient.GetAsync("/api/rlpt-bypass-auth")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonClient.GetAsync("/api/rlpt-bypass-auth")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
