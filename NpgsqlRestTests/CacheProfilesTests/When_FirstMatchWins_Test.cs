using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// Setup for When_FirstMatchWins_Test.
    /// Function takes `x` parameter; annotated with `cache_profile first_match_wins`. That profile defines
    /// two When rules whose conditions both match `x="a"`:
    ///   1. Skip = true
    ///   2. ThenExpiration = 1 hour
    /// First-match-wins means rule 1 (Skip) takes effect; rule 2 is ignored. We verify by sending two calls
    /// with x=a and observing that they each return a fresh UUID — the Skip prevented any caching.
    /// </summary>
    public static void When_FirstMatchWins_Test()
    {
        script.Append(@"
        create function cp_first_match_wins(x text)
        returns text language plpgsql as $$
        begin
            return x || ':' || gen_random_uuid()::text;
        end;
        $$;
        comment on function cp_first_match_wins(text) is '
        HTTP GET
        cache_profile first_match_wins
        ';
        ");
    }
}

[Collection("CacheProfilesTestFixture")]
public class When_FirstMatchWins_Test(CacheProfilesTestFixture test)
{
    /// <summary>
    /// Two When rules in the profile both match the same parameter value, but the first one (Skip) is
    /// expected to win — second rule (TTL override) is irrelevant. Two calls with the same param must
    /// each produce a fresh UUID (proving Skip applied → no caching).
    /// </summary>
    [Fact]
    public async Task First_matching_When_rule_takes_effect_subsequent_rules_are_ignored()
    {
        using var client = test.CreateClient();

        using var r1 = await client.GetAsync("/api/cp-first-match-wins/?x=a");
        var b1 = await r1.Content.ReadAsStringAsync();
        b1.Should().StartWith("a:");

        using var r2 = await client.GetAsync("/api/cp-first-match-wins/?x=a");
        var b2 = await r2.Content.ReadAsStringAsync();
        b2.Should().NotBe(b1, "first rule (Skip) wins → second call must compute fresh; if rule 2 (TTL) had won, b2 would equal b1");
    }
}
