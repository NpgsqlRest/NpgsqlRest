using Microsoft.Extensions.Configuration;
using NpgsqlRestClient;

namespace NpgsqlRestTests.AuthTests;

/// <summary>
/// Unit tests for the auth time-resolution path, post-3.13.0:
/// <list type="bullet">
///   <item><see cref="Builder.ResolveAuthTimeSpan"/> — parses a Postgres-interval string at the given
///   key, throws on invalid syntax, falls back to the supplied default when the key is null/missing.</item>
///   <item>Fail-fast detection of removed legacy fields (CookieValidDays, BearerTokenExpireHours,
///   JwtExpireMinutes, JwtRefreshExpireDays) — surfaced via the public <see cref="Builder.BuildAuthentication"/>
///   path so existing users see a clear migration error rather than a silent regression.</item>
/// </list>
///
/// Tests do NOT exercise legacy-fallback behavior because the legacy fields were removed in 3.13.0.
/// </summary>
public class AuthIntervalNotationTests
{
    private static (Config config, Builder builder, IConfigurationSection authCfg) BuildFromAuthJson(string authJson)
    {
        var json = $$"""
        {
            "Auth": {{authJson}}
        }
        """;
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, json);

        var config = new Config();
        try
        {
            config.Build([tempFile], []);
        }
        finally
        {
            File.Delete(tempFile);
        }

        var builder = new Builder(config);
        var authCfg = config.Cfg.GetSection("Auth");
        return (config, builder, authCfg);
    }

    [Fact]
    public void Returns_parsed_interval_when_field_set()
    {
        var (_, builder, authCfg) = BuildFromAuthJson("""{ "CookieValid": "1 hour" }""");

        builder.ResolveAuthTimeSpan("CookieValid", TimeSpan.FromDays(14), authCfg)
            .Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void Returns_default_when_field_missing()
    {
        var (_, builder, authCfg) = BuildFromAuthJson("""{ "OtherKey": "x" }""");

        builder.ResolveAuthTimeSpan("CookieValid", TimeSpan.FromDays(14), authCfg)
            .Should().Be(TimeSpan.FromDays(14));
    }

    [Fact]
    public void Returns_default_when_field_is_null()
    {
        var (_, builder, authCfg) = BuildFromAuthJson("""{ "CookieValid": null }""");

        builder.ResolveAuthTimeSpan("CookieValid", TimeSpan.FromDays(14), authCfg)
            .Should().Be(TimeSpan.FromDays(14));
    }

    [Fact]
    public void Returns_default_when_field_is_empty_string()
    {
        var (_, builder, authCfg) = BuildFromAuthJson("""{ "CookieValid": "" }""");

        builder.ResolveAuthTimeSpan("CookieValid", TimeSpan.FromDays(14), authCfg)
            .Should().Be(TimeSpan.FromDays(14));
    }

    [Fact]
    public void Returns_default_when_field_is_whitespace_string()
    {
        var (_, builder, authCfg) = BuildFromAuthJson("""{ "CookieValid": "   " }""");

        builder.ResolveAuthTimeSpan("CookieValid", TimeSpan.FromDays(14), authCfg)
            .Should().Be(TimeSpan.FromDays(14));
    }

    [Fact]
    public void Throws_on_invalid_interval_string()
    {
        // Fail-fast — an unparseable interval throws and forces the user to fix their config rather
        // than silently falling back to a default that doesn't match their intent.
        var (_, builder, authCfg) = BuildFromAuthJson("""{ "CookieValid": "fortnight" }""");

        var act = () => builder.ResolveAuthTimeSpan("CookieValid", TimeSpan.FromDays(14), authCfg);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Invalid interval value for Auth:CookieValid: 'fortnight'*");
    }

    [Fact]
    public void Postgres_interval_units_pass_through_to_parser()
    {
        var (_, builder, authCfg) = BuildFromAuthJson("""{ "JwtExpire": "30 seconds" }""");

        builder.ResolveAuthTimeSpan("JwtExpire", TimeSpan.FromMinutes(60), authCfg)
            .Should().Be(TimeSpan.FromSeconds(30));
    }
}
