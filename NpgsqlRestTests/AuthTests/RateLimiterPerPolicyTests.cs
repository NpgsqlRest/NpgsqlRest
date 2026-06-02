using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// SQL setup for the per-policy rejection-message tests. Three trivial functions, each tied to one of
    /// the three policies registered by <see cref="RateLimiterPerPolicyTestFixture"/>:
    ///
    ///   - <c>rlpm_login()</c>   → policy "rlpm-login"   (message override, status inherits global 429)
    ///   - <c>rlpm_strict()</c>  → policy "rlpm-strict"  (status 503 + message override)
    ///   - <c>rlpm_inherit()</c> → policy "rlpm-inherit" (no override; inherits global 429 + global message)
    ///
    /// Each policy has PermitLimit=1, so the first call returns "ok" and the second returns the configured
    /// rejection. Each policy is exercised by exactly one test, so the single shared bucket is not contended.
    /// </summary>
    public static void RateLimiterPerPolicyTests()
    {
        script.Append("""

        create function rlpm_login()
        returns text language sql as $$ select 'ok' $$;
        comment on function rlpm_login() is '
        HTTP GET
        rate_limiter rlpm-login
        ';

        create function rlpm_strict()
        returns text language sql as $$ select 'ok' $$;
        comment on function rlpm_strict() is '
        HTTP GET
        rate_limiter rlpm-strict
        ';

        create function rlpm_inherit()
        returns text language sql as $$ select 'ok' $$;
        comment on function rlpm_inherit() is '
        HTTP GET
        rate_limiter rlpm-inherit
        ';
""");
    }
}

[Collection("RateLimiterPerPolicyTestFixture")]
public class RateLimiterPerPolicyTests(RateLimiterPerPolicyTestFixture test)
{
    /// <summary>
    /// Policy with a StatusMessage override but no StatusCode override: the rejection body is the policy's
    /// own message, while the status code falls back to the global 429.
    /// </summary>
    [Fact]
    public async Task Policy_with_message_override_returns_its_own_message_and_global_status()
    {
        using var client = test.CreateClient();

        using var ok = await client.GetAsync("/api/rlpm-login");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadAsStringAsync()).Should().Be("ok");

        using var rejected = await client.GetAsync("/api/rlpm-login");
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await rejected.Content.ReadAsStringAsync()).Should().Be(RateLimiterPerPolicyTestFixture.LoginMessage);
    }

    /// <summary>
    /// Policy overriding BOTH status and message: the rejection returns 503 and the policy's message,
    /// not the global 429.
    /// </summary>
    [Fact]
    public async Task Policy_with_status_and_message_override_returns_both()
    {
        using var client = test.CreateClient();

        using var ok = await client.GetAsync("/api/rlpm-strict");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadAsStringAsync()).Should().Be("ok");

        using var rejected = await client.GetAsync("/api/rlpm-strict");
        rejected.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await rejected.Content.ReadAsStringAsync()).Should().Be(RateLimiterPerPolicyTestFixture.StrictMessage);
    }

    /// <summary>
    /// Policy with no override inherits the global status code and global message — proving the per-policy
    /// overrides do not leak across policies and the global default still applies.
    /// </summary>
    [Fact]
    public async Task Policy_without_override_inherits_global_status_and_message()
    {
        using var client = test.CreateClient();

        using var ok = await client.GetAsync("/api/rlpm-inherit");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadAsStringAsync()).Should().Be("ok");

        using var rejected = await client.GetAsync("/api/rlpm-inherit");
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await rejected.Content.ReadAsStringAsync()).Should().Be(RateLimiterPerPolicyTestFixture.GlobalMessage);
    }
}
