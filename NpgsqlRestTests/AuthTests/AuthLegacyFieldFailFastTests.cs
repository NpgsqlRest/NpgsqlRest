using System.Reflection;
using Microsoft.Extensions.Configuration;
using NpgsqlRestClient;

namespace NpgsqlRestTests.AuthTests;

/// <summary>
/// Fail-fast tests for the four removed legacy auth time fields. In 3.13.0, the integer-based
/// <c>CookieValidDays</c>, <c>BearerTokenExpireHours</c>, <c>JwtExpireMinutes</c>, and
/// <c>JwtRefreshExpireDays</c> fields were removed in favor of interval notation. To prevent silent
/// regressions for users upgrading with their old config still in place, presence of any of the
/// four fields is detected at startup and triggers an <see cref="InvalidOperationException"/> with
/// a clear migration message.
///
/// Test seam: <c>Builder.DetectLegacyAuthTimeFields</c> is private. We invoke it via reflection so
/// each scenario is testable without spinning up a full WebApplication. (The same method is invoked
/// from the public <c>BuildAuthentication</c> entry point — covered by integration tests.)
/// </summary>
public class AuthLegacyFieldFailFastTests
{
    private static (Builder builder, IConfigurationSection authCfg) BuildFromAuthJson(string authJson)
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

        return (new Builder(config), config.Cfg.GetSection("Auth"));
    }

    private static void InvokeDetectLegacyFields(Builder builder, IConfigurationSection authCfg)
    {
        // DetectLegacyAuthTimeFields is private — reflection is the lowest-friction way to test it
        // directly. The exception is unwrapped from TargetInvocationException so assertions read
        // naturally.
        var method = typeof(Builder).GetMethod("DetectLegacyAuthTimeFields",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("DetectLegacyAuthTimeFields not found on Builder");
        try
        {
            method.Invoke(builder, [authCfg]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    [Fact]
    public void No_legacy_fields_present_passes()
    {
        var (builder, authCfg) = BuildFromAuthJson("""{ "CookieValid": "14 days" }""");

        var act = () => InvokeDetectLegacyFields(builder, authCfg);

        act.Should().NotThrow();
    }

    [Fact]
    public void CookieValidDays_present_throws_with_migration_hint()
    {
        var (builder, authCfg) = BuildFromAuthJson("""{ "CookieValidDays": 30 }""");

        var act = () => InvokeDetectLegacyFields(builder, authCfg);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Auth:CookieValidDays has been removed in 3.13.0*Auth:CookieValid*\"14 days\"*");
    }

    [Fact]
    public void BearerTokenExpireHours_present_throws_with_migration_hint()
    {
        var (builder, authCfg) = BuildFromAuthJson("""{ "BearerTokenExpireHours": 1 }""");

        var act = () => InvokeDetectLegacyFields(builder, authCfg);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Auth:BearerTokenExpireHours has been removed in 3.13.0*Auth:BearerTokenExpire*\"1 hour\"*");
    }

    [Fact]
    public void JwtExpireMinutes_present_throws_with_migration_hint()
    {
        var (builder, authCfg) = BuildFromAuthJson("""{ "JwtExpireMinutes": 60 }""");

        var act = () => InvokeDetectLegacyFields(builder, authCfg);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Auth:JwtExpireMinutes has been removed in 3.13.0*Auth:JwtExpire*\"60 minutes\"*");
    }

    [Fact]
    public void JwtRefreshExpireDays_present_throws_with_migration_hint()
    {
        var (builder, authCfg) = BuildFromAuthJson("""{ "JwtRefreshExpireDays": 7 }""");

        var act = () => InvokeDetectLegacyFields(builder, authCfg);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Auth:JwtRefreshExpireDays has been removed in 3.13.0*Auth:JwtRefreshExpire*\"7 days\"*");
    }

    [Fact]
    public void First_legacy_field_present_throws_immediately()
    {
        // Multiple legacy fields present — first-detected throws (we don't need to enumerate all of
        // them; the user fixes one and re-runs).
        var (builder, authCfg) = BuildFromAuthJson("""
        { "CookieValidDays": 30, "JwtExpireMinutes": 60 }
        """);

        var act = () => InvokeDetectLegacyFields(builder, authCfg);

        act.Should().Throw<InvalidOperationException>();
    }
}
