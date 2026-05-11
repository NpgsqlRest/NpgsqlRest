using NpgsqlRest.OpenAPI;
using NpgsqlRestClient;

namespace NpgsqlRestTests.OpenApiTests;

/// <summary>
/// Verifies that the new OpenAPI filter knobs reach <see cref="OpenApiOptions"/> when configured
/// through <c>appsettings.json</c> — i.e. that the wiring inside
/// <see cref="App.BuildOpenApiOptions(string?)"/> reads each key correctly. This catches typos in
/// the config-key strings and mismatches between the JSON shape (single value vs array, bool vs
/// enum) and the property type — failures that the plugin-level filter tests can't catch because
/// they bypass the client config layer entirely.
///
/// Each test writes a temp <c>appsettings.json</c>, builds a <see cref="Config"/> from it, and
/// invokes <see cref="App.BuildOpenApiOptions(string?)"/> directly — no WebApplication, no DB
/// connection, no plugin registration. The returned <see cref="OpenApiOptions"/> is asserted
/// against expected values.
/// </summary>
public class OpenApiClientConfigTests
{
    private static OpenApiOptions? BuildFromJson(string npgsqlRestSection)
    {
        var json = $$"""
        {
            "NpgsqlRest": {{npgsqlRestSection}}
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

        // App's constructor takes a Builder, but BuildOpenApiOptions only reads from _config. A
        // bare-bones Builder satisfies the constructor.
        var builder = new Builder(config);
        var app = new App(config, builder);
        return app.BuildOpenApiOptions(connectionString: null);
    }

    // ------------------------------------------------------------------------
    // Section presence / Enabled gate
    // ------------------------------------------------------------------------

    [Fact]
    public void Disabled_section_returns_null()
    {
        // Existing default — when OpenApiOptions.Enabled is false, no options are built at all so
        // the plugin isn't registered. Verifies the early-return path.
        var opts = BuildFromJson("""{ "OpenApiOptions": { "Enabled": false } }""");
        opts.Should().BeNull();
    }

    [Fact]
    public void Missing_section_returns_null()
    {
        var opts = BuildFromJson("""{ }""");
        opts.Should().BeNull();
    }

    // ------------------------------------------------------------------------
    // New filter knobs — each in isolation
    // ------------------------------------------------------------------------

    [Fact]
    public void IncludeSchemas_array_reaches_options()
    {
        var opts = BuildFromJson("""
        {
            "OpenApiOptions": {
                "Enabled": true,
                "FileName": "/tmp/o.json",
                "IncludeSchemas": ["partner", "billing"]
            }
        }
        """);

        opts.Should().NotBeNull();
        opts!.IncludeSchemas.Should().BeEquivalentTo(new[] { "partner", "billing" });
    }

    [Fact]
    public void IncludeSchemas_empty_array_treated_as_null_for_filter_purposes()
    {
        // The plugin treats empty array == null == "no filter" (via the `{ Length: > 0 }` pattern).
        // The config layer can return either null or an empty array depending on how Microsoft.
        // Extensions.Configuration handles `[]` — both must produce a config that means "no filter".
        var opts = BuildFromJson("""
        {
            "OpenApiOptions": {
                "Enabled": true,
                "FileName": "/tmp/o.json",
                "IncludeSchemas": []
            }
        }
        """);

        opts.Should().NotBeNull();
        // Either null or empty array is acceptable here — both result in "no filter" in the plugin.
        (opts!.IncludeSchemas is null || opts.IncludeSchemas.Length == 0).Should().BeTrue();
    }

    [Fact]
    public void ExcludeSchemas_array_reaches_options()
    {
        var opts = BuildFromJson("""
        {
            "OpenApiOptions": {
                "Enabled": true,
                "FileName": "/tmp/o.json",
                "ExcludeSchemas": ["internal", "audit"]
            }
        }
        """);

        opts.Should().NotBeNull();
        opts!.ExcludeSchemas.Should().BeEquivalentTo(new[] { "internal", "audit" });
    }

    [Fact]
    public void NameSimilarTo_string_reaches_options()
    {
        var opts = BuildFromJson("""
        {
            "OpenApiOptions": {
                "Enabled": true,
                "FileName": "/tmp/o.json",
                "NameSimilarTo": "partner_%"
            }
        }
        """);

        opts.Should().NotBeNull();
        opts!.NameSimilarTo.Should().Be("partner_%");
    }

    [Fact]
    public void NameNotSimilarTo_string_reaches_options()
    {
        var opts = BuildFromJson("""
        {
            "OpenApiOptions": {
                "Enabled": true,
                "FileName": "/tmp/o.json",
                "NameNotSimilarTo": "%_admin"
            }
        }
        """);

        opts.Should().NotBeNull();
        opts!.NameNotSimilarTo.Should().Be("%_admin");
    }

    [Fact]
    public void RequiresAuthorizationOnly_true_reaches_options()
    {
        var opts = BuildFromJson("""
        {
            "OpenApiOptions": {
                "Enabled": true,
                "FileName": "/tmp/o.json",
                "RequiresAuthorizationOnly": true
            }
        }
        """);

        opts.Should().NotBeNull();
        opts!.RequiresAuthorizationOnly.Should().BeTrue();
    }

    [Fact]
    public void RequiresAuthorizationOnly_defaults_to_false_when_unset()
    {
        var opts = BuildFromJson("""
        {
            "OpenApiOptions": {
                "Enabled": true,
                "FileName": "/tmp/o.json"
            }
        }
        """);

        opts.Should().NotBeNull();
        opts!.RequiresAuthorizationOnly.Should().BeFalse(
            "the default is the existing behavior — document everything");
    }

    // ------------------------------------------------------------------------
    // Full partner-config example from the changelog — proves the realistic
    // "external Web API service" configuration produces the expected options.
    // ------------------------------------------------------------------------

    [Fact]
    public void Partner_facing_full_config_round_trips()
    {
        // The exact shape recommended in the v3.15.0 changelog for partner-facing documents.
        // This test fails if anything in App.BuildOpenApiOptions silently drops a key.
        var opts = BuildFromJson("""
        {
            "OpenApiOptions": {
                "Enabled": true,
                "FileName": "openapi-partner.json",
                "UrlPath": "/openapi/partner.json",
                "DocumentTitle": "Acme Partner API",
                "DocumentDescription": "JWT-authenticated REST surface for partner integrations.",
                "DocumentVersion": "2.1.0",
                "IncludeSchemas": ["partner"],
                "NameNotSimilarTo": "%_admin",
                "RequiresAuthorizationOnly": true,
                "AddCurrentServer": false,
                "Servers": [
                    { "Url": "https://api.acme.com", "Description": "Production" }
                ],
                "SecuritySchemes": [
                    { "Name": "bearerAuth", "Type": "Http", "Scheme": "Bearer", "BearerFormat": "JWT" }
                ]
            }
        }
        """);

        opts.Should().NotBeNull();
        opts!.FileName.Should().Be("openapi-partner.json");
        opts.UrlPath.Should().Be("/openapi/partner.json");
        opts.DocumentTitle.Should().Be("Acme Partner API");
        opts.DocumentDescription.Should().Be("JWT-authenticated REST surface for partner integrations.");
        opts.DocumentVersion.Should().Be("2.1.0");
        opts.IncludeSchemas.Should().BeEquivalentTo(new[] { "partner" });
        opts.ExcludeSchemas.Should().BeNullOrEmpty("ExcludeSchemas wasn't set in this config");
        opts.NameSimilarTo.Should().BeNull();
        opts.NameNotSimilarTo.Should().Be("%_admin");
        opts.RequiresAuthorizationOnly.Should().BeTrue();
        opts.AddCurrentServer.Should().BeFalse();

        opts.Servers.Should().NotBeNull();
        opts.Servers!.Length.Should().Be(1);
        opts.Servers[0].Url.Should().Be("https://api.acme.com");
        opts.Servers[0].Description.Should().Be("Production");

        opts.SecuritySchemes.Should().NotBeNull();
        opts.SecuritySchemes!.Length.Should().Be(1);
        opts.SecuritySchemes[0].Name.Should().Be("bearerAuth");
        opts.SecuritySchemes[0].Type.Should().Be(OpenApiSecuritySchemeType.Http);
        opts.SecuritySchemes[0].Scheme.Should().Be(HttpAuthScheme.Bearer);
        opts.SecuritySchemes[0].BearerFormat.Should().Be("JWT");
    }

    // ------------------------------------------------------------------------
    // Defaults preserved for unset filter keys — backward compatibility.
    // ------------------------------------------------------------------------

    [Fact]
    public void Minimal_config_leaves_all_new_filters_unset()
    {
        // Verifies "existing appsettings.json works as-is" claim from the changelog. A 3.14-shape
        // config without any of the new keys must produce options that don't filter anything.
        var opts = BuildFromJson("""
        {
            "OpenApiOptions": {
                "Enabled": true,
                "FileName": "/tmp/o.json",
                "UrlPath": "/openapi.json",
                "DocumentTitle": "Old App"
            }
        }
        """);

        opts.Should().NotBeNull();
        opts!.IncludeSchemas.Should().BeNullOrEmpty();
        opts.ExcludeSchemas.Should().BeNullOrEmpty();
        opts.NameSimilarTo.Should().BeNull();
        opts.NameNotSimilarTo.Should().BeNull();
        opts.RequiresAuthorizationOnly.Should().BeFalse(
            "unset RequiresAuthorizationOnly must default to false (existing behavior)");
    }
}
