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

    // -----------------------------------------------------------------------------------------------
    // RateLimiterOptions:Policies — type-discriminated by Type (FixedWindow / SlidingWindow /
    // TokenBucket / Concurrency). The defaults ship example names (fixed/sliding/bucket/concurrency/
    // per_user); custom names must validate by Type, not by name. Bug doc:
    // RATE_LIMITER_CONFIG_VALIDATION_FIX.md.
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void RateLimiter_PolicyWithCustomName_FixedWindow_AcceptsAllKeys()
    {
        var actual = ActualFromJson("""
        {
          "RateLimiterOptions": {
            "Policies": {
              "login_throttle": {
                "Type": "FixedWindow",
                "Enabled": true,
                "PermitLimit": 10,
                "WindowSeconds": 60,
                "QueueLimit": 0,
                "AutoReplenishment": true,
                "Partition": {
                  "Sources": [{ "Type": "IpAddress" }],
                  "BypassAuthenticated": false
                }
              }
            }
          }
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void RateLimiter_PolicyWithCustomName_TokenBucket_AcceptsAllKeys()
    {
        var actual = ActualFromJson("""
        {
          "RateLimiterOptions": {
            "Policies": {
              "api_burst": {
                "Type": "TokenBucket",
                "Enabled": true,
                "TokenLimit": 100,
                "TokensPerPeriod": 10,
                "ReplenishmentPeriodSeconds": 1,
                "QueueLimit": 0,
                "AutoReplenishment": true
              }
            }
          }
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void RateLimiter_MultipleCustomPolicies_EachValidatedByOwnType()
    {
        var actual = ActualFromJson("""
        {
          "RateLimiterOptions": {
            "Policies": {
              "login_throttle":      { "Type": "FixedWindow",   "PermitLimit": 10, "WindowSeconds": 60 },
              "search_window":       { "Type": "SlidingWindow", "PermitLimit": 50, "WindowSeconds": 60, "SegmentsPerWindow": 6 },
              "api_burst":           { "Type": "TokenBucket",   "TokenLimit": 100, "TokensPerPeriod": 10, "ReplenishmentPeriodSeconds": 1 },
              "compute_concurrency": { "Type": "Concurrency",   "PermitLimit": 4, "QueueLimit": 0, "OldestFirst": true }
            }
          }
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void RateLimiter_ExampleNames_StillValidate()
    {
        // The example names (fixed/sliding/bucket/concurrency/per_user) must still validate cleanly
        // — regression test against a fix that broke them.
        var actual = ActualFromJson("""
        {
          "RateLimiterOptions": {
            "Policies": {
              "fixed":       { "Type": "FixedWindow",   "PermitLimit": 100, "WindowSeconds": 60 },
              "sliding":     { "Type": "SlidingWindow", "PermitLimit": 100, "WindowSeconds": 60, "SegmentsPerWindow": 6 },
              "bucket":      { "Type": "TokenBucket",   "TokenLimit": 100, "TokensPerPeriod": 10, "ReplenishmentPeriodSeconds": 10 },
              "concurrency": { "Type": "Concurrency",   "PermitLimit": 10, "QueueLimit": 5, "OldestFirst": true }
            }
          }
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void RateLimiter_TypoInsidePolicy_Flagged()
    {
        // Type-discriminated validation must still catch typos inside a policy.
        var actual = ActualFromJson("""
        {
          "RateLimiterOptions": {
            "Policies": {
              "login_throttle": {
                "Type": "FixedWindow",
                "PermitLimt": 10
              }
            }
          }
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().ContainSingle()
            .Which.Should().Be("RateLimiterOptions:Policies:login_throttle:PermitLimt");
    }

    [Fact]
    public void RateLimiter_CrossTypeKey_Flagged()
    {
        // TokensPerPeriod belongs to TokenBucket — placing it on a FixedWindow policy is a real error.
        var actual = ActualFromJson("""
        {
          "RateLimiterOptions": {
            "Policies": {
              "login_throttle": {
                "Type": "FixedWindow",
                "TokensPerPeriod": 10
              }
            }
          }
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().ContainSingle()
            .Which.Should().Be("RateLimiterOptions:Policies:login_throttle:TokensPerPeriod");
    }

    [Fact]
    public void RateLimiter_PolicyMissingType_NotValidated()
    {
        // Same precedent as Auth:Schemes — no Type means BuildRateLimiter silently skips the policy,
        // so the validator silently skips it too.
        var actual = ActualFromJson("""
        {
          "RateLimiterOptions": {
            "Policies": {
              "login_throttle": {
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
    public void RateLimiter_PartitionSubBlock_Validates()
    {
        // Partition is part of every per-type schema. Typos inside Partition should surface.
        var actual = ActualFromJson("""
        {
          "RateLimiterOptions": {
            "Policies": {
              "login_throttle": {
                "Type": "FixedWindow",
                "Partition": {
                  "Sources": [{ "Type": "IpAddress" }],
                  "BypassAuthnticated": true
                }
              }
            }
          }
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().ContainSingle()
            .Which.Should().Be("RateLimiterOptions:Policies:login_throttle:Partition:BypassAuthnticated");
    }

    // -----------------------------------------------------------------------------------------------
    // CacheOptions:Profiles — flat shared schema across cache types (Memory / Redis / Hybrid).
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void CacheProfile_CustomName_AcceptsAllKeys()
    {
        var actual = ActualFromJson("""
        {
          "CacheOptions": {
            "Profiles": {
              "session_cache": {
                "Enabled": true,
                "Type": "Memory",
                "Expiration": "10 minutes",
                "Parameters": ["user_id"]
              }
            }
          }
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void CacheProfile_WhenRule_Validates()
    {
        var actual = ActualFromJson("""
        {
          "CacheOptions": {
            "Profiles": {
              "session_cache": {
                "Enabled": true,
                "Type": "Memory",
                "When": [
                  { "Parameter": "limit", "Value": "0", "Then": "skip" }
                ]
              }
            }
          }
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void CacheProfile_TypoInsideProfile_Flagged()
    {
        var actual = ActualFromJson("""
        {
          "CacheOptions": {
            "Profiles": {
              "session_cache": {
                "Enabled": true,
                "Type": "Memory",
                "Expirashun": "10 minutes"
              }
            }
          }
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().ContainSingle()
            .Which.Should().Be("CacheOptions:Profiles:session_cache:Expirashun");
    }

    [Fact]
    public void CacheProfile_ExampleNames_StillValidate()
    {
        // The example names (fast_memory/shared_redis/date_range_hybrid) must continue to pass.
        var actual = ActualFromJson("""
        {
          "CacheOptions": {
            "Profiles": {
              "fast_memory":       { "Enabled": false, "Type": "Memory", "Expiration": "30 seconds" },
              "shared_redis":      { "Enabled": false, "Type": "Redis",  "Expiration": "1 hour" },
              "date_range_hybrid": { "Enabled": false, "Type": "Hybrid", "Expiration": "5 minutes" }
            }
          }
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------------------------------
    // ValidationOptions:Rules — flat shared schema across validation types. Previously in the open-dict
    // whitelist (which let typos pass silently); now type-discriminated so typos inside any rule are
    // caught.
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void ValidationRule_CustomName_AcceptsAllKeys()
    {
        var actual = ActualFromJson("""
        {
          "ValidationOptions": {
            "Rules": {
              "phone_number": {
                "Type": "Regex",
                "Pattern": "^[0-9-]+$",
                "MinLength": 7,
                "MaxLength": 15,
                "Message": "Invalid phone number",
                "StatusCode": 422
              }
            }
          }
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void ValidationRule_TypoInsideRule_Flagged()
    {
        var actual = ActualFromJson("""
        {
          "ValidationOptions": {
            "Rules": {
              "phone_number": {
                "Type": "Regex",
                "Patrn": "^[0-9]+$"
              }
            }
          }
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().ContainSingle()
            .Which.Should().Be("ValidationOptions:Rules:phone_number:Patrn");
    }

    [Fact]
    public void ValidationRule_ExampleNames_StillValidate()
    {
        var actual = ActualFromJson("""
        {
          "ValidationOptions": {
            "Rules": {
              "not_null":  { "Type": "NotNull",  "Message": "is null",  "StatusCode": 400 },
              "not_empty": { "Type": "NotEmpty", "Message": "is empty", "StatusCode": 400 },
              "required":  { "Type": "Required", "Message": "required", "StatusCode": 400 },
              "email":     { "Type": "Regex",    "Pattern": "x", "Message": "bad",     "StatusCode": 400 }
            }
          }
        }
        """);

        var warnings = ConfigDefaults.FindUnknownConfigKeys(Defaults(), actual);

        warnings.Should().BeEmpty();
    }
}
