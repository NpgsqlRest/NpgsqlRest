using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// Setup for Profile_ExpirationOverride_Test.
    /// Function annotated with `cache_profile short_ttl` (1s TTL) AND `cache_expires 1 hour`.
    /// The endpoint annotation must override the profile's Expiration — so the entry stays
    /// fresh well beyond 1 second.
    /// </summary>
    public static void Profile_ExpirationOverride_Test()
    {
        script.Append(@"
        create function cp_expiration_override()
        returns text language plpgsql as $$
        begin
            return gen_random_uuid()::text;
        end;
        $$;
        comment on function cp_expiration_override() is '
        HTTP GET
        cache_profile short_ttl
        cache_expires 1 hour
        ';
        ");
    }
}

[Collection("CacheProfilesTestFixture")]
public class Profile_ExpirationOverride_Test(CacheProfilesTestFixture test)
{
    /// <summary>
    /// When the endpoint sets its own `cache_expires` annotation, that value must override the
    /// profile's `Expiration`. The "short_ttl" profile would otherwise expire after 1 second; the
    /// `cache_expires 1 hour` annotation should keep the cached value alive after a 2-second wait.
    /// </summary>
    [Fact]
    public async Task Endpoint_cache_expires_annotation_overrides_profile_Expiration()
    {
        using var client = test.CreateClient();

        using var r1 = await client.GetAsync("/api/cp-expiration-override/");
        var body1 = await r1.Content.ReadAsStringAsync();

        await Task.Delay(TimeSpan.FromSeconds(2));

        using var r2 = await client.GetAsync("/api/cp-expiration-override/");
        var body2 = await r2.Content.ReadAsStringAsync();
        body2.Should().Be(body1, "annotation 1-hour expiry should keep entry alive past profile's 1-second TTL");
    }
}
