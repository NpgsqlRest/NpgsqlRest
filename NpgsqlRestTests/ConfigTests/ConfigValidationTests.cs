using NpgsqlRestClient;

namespace NpgsqlRestTests.ConfigTests;

/// <summary>
/// Unit tests for <see cref="ConfigDefaults.FindUnknownConfigKeys"/>. The validator walks an actual
/// config tree against the defaults schema and returns paths to keys not present in defaults. Key
/// invariant: under <c>Auth:Schemes:&lt;name&gt;</c>, valid keys depend on the scheme's <c>Type</c>
/// field, NOT on the name. Prior to the fix in this commit, named schemes whose names happened to
/// match a documented example (<c>short_session</c> / <c>api_token</c> / <c>admin_jwt</c>) were
/// validated against the partial example schema, falsely flagging valid per-type override keys.
/// </summary>
public class ConfigValidationTests
{
    private static JsonObject Defaults() => ConfigDefaults.GetDefaults();

    private static JsonObject ActualFromJson(string json)
    {
        var node = JsonNode.Parse(json) ?? throw new InvalidOperationException("Bad JSON");
        return node.AsObject();
    }

    [Fact]
    public void Cookies_SchemeWithExampleName_AcceptsAllPerTypeKeys()
    {
        // short_session is a documented example name; it must not lock validation to the partial
        // example schema. Every Cookies-type override key must validate clean.
        var actual = ActualFromJson("""
        {
          "Auth": {
            "Schemes": {
              "short_session": {
                "Type": "Cookies",
                "Enabled": true,
                "CookieValid": "1 hour",
                "CookieName": "my_app",
                "CookiePath": "/",
                "CookieDomain": "example.com",
                "CookieMultiSessions": false,
                "CookieHttpOnly": true,
                "CookieSameSite": "Strict",
                "CookieSecure": "Always"
              }
            }
          }
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void BearerToken_SchemeWithExampleName_AcceptsAllPerTypeKeys()
    {
        var actual = ActualFromJson("""
        {
          "Auth": {
            "Schemes": {
              "api_token": {
                "Type": "BearerToken",
                "Enabled": true,
                "BearerTokenExpire": "2 hours",
                "BearerTokenRefreshPath": "/api/api-token/refresh"
              }
            }
          }
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Jwt_SchemeWithExampleName_AcceptsAllPerTypeKeys()
    {
        var actual = ActualFromJson("""
        {
          "Auth": {
            "Schemes": {
              "admin_jwt": {
                "Type": "Jwt",
                "Enabled": true,
                "JwtSecret": "01234567890123456789012345678901",
                "JwtIssuer": "issuer",
                "JwtAudience": "audience",
                "JwtExpire": "5 minutes",
                "JwtRefreshExpire": "1 hour",
                "JwtClockSkew": "30 seconds",
                "JwtValidateIssuer": true,
                "JwtValidateAudience": true,
                "JwtValidateLifetime": true,
                "JwtValidateIssuerSigningKey": true,
                "JwtRefreshPath": "/api/admin-jwt/refresh"
              }
            }
          }
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void CustomSchemeName_ValidatesByType_NotByName()
    {
        // A name not in the docs-example set must still go through per-type validation — not the old
        // open-dict skip path that left typos undetected.
        var actual = ActualFromJson("""
        {
          "Auth": {
            "Schemes": {
              "my_custom_scheme": {
                "Type": "Cookies",
                "CookieValid": "30 days",
                "CookieHttpOnly": false
              }
            }
          }
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Cookies_InvalidKey_StillFlagged()
    {
        // Per-type validation must still catch typos like Cookies-type scheme with a bogus key.
        var actual = ActualFromJson("""
        {
          "Auth": {
            "Schemes": {
              "short_session": {
                "Type": "Cookies",
                "CookieHttpOnly": true,
                "CooieName": "typo"
              }
            }
          }
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().ContainSingle()
            .Which.Should().Be("Auth:Schemes:short_session:CooieName");
    }

    [Fact]
    public void Cookies_JwtKeyOnCookieScheme_Flagged()
    {
        // Bug A regression: a Jwt-only key on a Cookies-type scheme is a real error, not a valid
        // override. Type-discriminated schema must reject it.
        var actual = ActualFromJson("""
        {
          "Auth": {
            "Schemes": {
              "my_scheme": {
                "Type": "Cookies",
                "JwtSecret": "wrong_type_key"
              }
            }
          }
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().ContainSingle()
            .Which.Should().Be("Auth:Schemes:my_scheme:JwtSecret");
    }

    [Fact]
    public void Scheme_MissingType_NotValidated()
    {
        // No Type field → can't pick a schema. Validator skips silently; RegisterAuthSchemes throws a
        // clear startup error. Don't double-report.
        var actual = ActualFromJson("""
        {
          "Auth": {
            "Schemes": {
              "my_scheme": {
                "Enabled": true,
                "FooBar": "anything"
              }
            }
          }
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Scheme_InvalidType_NotValidated()
    {
        // Unknown Type value → schema lookup returns null; same as missing Type. RegisterAuthSchemes
        // surfaces the clearer error.
        var actual = ActualFromJson("""
        {
          "Auth": {
            "Schemes": {
              "my_scheme": {
                "Type": "Something",
                "FooBar": "anything"
              }
            }
          }
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void MultipleSchemes_EachValidatedByOwnType()
    {
        var actual = ActualFromJson("""
        {
          "Auth": {
            "Schemes": {
              "short_session": { "Type": "Cookies", "CookieName": "ok" },
              "api_token":     { "Type": "BearerToken", "BearerTokenExpire": "1 hour" },
              "admin_jwt":     { "Type": "Jwt", "JwtIssuer": "x" }
            }
          }
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void TopLevelUnknown_StillFlagged()
    {
        // Regression: existing top-level unknown-key behavior must still work.
        var actual = ActualFromJson("""
        {
          "TotallyMadeUp": "value"
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().Contain("TotallyMadeUp");
    }
}
