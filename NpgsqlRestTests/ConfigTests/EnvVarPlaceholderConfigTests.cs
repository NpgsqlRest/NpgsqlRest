using Microsoft.Extensions.Configuration;
using NpgsqlRestClient;

namespace NpgsqlRestTests.ConfigTests;

/// <summary>
/// Environment-variable placeholders in configuration values (with <c>Config:ParseEnvironmentVariables</c>
/// enabled), applied to every config value type:
/// <list type="bullet">
/// <item><c>{NAME}</c> - optional: resolves to the variable's value when set; left untouched when not set
/// (so non-env brace syntax like Serilog templates survives, and typed bool/int reads fall back to default).</item>
/// <item><c>{!NAME}</c> - required: resolves to the variable's value, or throws at startup when it is not set.</item>
/// </list>
/// Previously a missing <c>{NAME}</c> crashed typed reads
/// (e.g. "Invalid boolean value '{GITHUB_AUTH_ENABLED}' for configuration key 'Enabled'").
/// </summary>
public class EnvVarPlaceholderConfigTests
{
    // Builds a Config with ParseEnvironmentVariables enabled, capturing the given env vars into EnvDict.
    private static Config CreateConfig(Dictionary<string, string> envVars)
    {
        foreach (var kvp in envVars)
        {
            Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
        }
        var config = new Config();
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """{ "Config": { "ParseEnvironmentVariables": true } }""");
        try
        {
            config.Build([tempFile], []);
        }
        finally
        {
            File.Delete(tempFile);
            foreach (var kvp in envVars)
            {
                Environment.SetEnvironmentVariable(kvp.Key, null);
            }
        }
        return config;
    }

    private static IConfigurationSection Section(string key, string value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { $"Auth:{key}", value } })
            .Build()
            .GetSection("Auth");

    // ---- ResolveEnv directly (the shared resolver used by every config read) ----

    [Fact]
    public void ResolveEnv_Optional_Present_Substitutes()
    {
        var config = CreateConfig(new() { { "BUILD_LABEL", "demo" } });
        config.ResolveEnv("label={BUILD_LABEL}").Should().Be("label=demo");
    }

    [Fact]
    public void ResolveEnv_Optional_Missing_LeftUntouched_PreservesNonEnvBraces()
    {
        var config = CreateConfig([]);
        // unset optional token is preserved verbatim - this is what keeps Serilog templates intact
        config.ResolveEnv("label={BUILD_LABEL};").Should().Be("label={BUILD_LABEL};");
        config.ResolveEnv("[{Timestamp:HH:mm:ss} {Message}]").Should().Be("[{Timestamp:HH:mm:ss} {Message}]");
    }

    [Fact]
    public void ResolveEnv_Required_Present_Substitutes()
    {
        var config = CreateConfig(new() { { "DB_HOST", "db.internal" } });
        config.ResolveEnv("host={!DB_HOST}").Should().Be("host=db.internal");
    }

    [Fact]
    public void ResolveEnv_Required_Missing_Throws_NamingVariable()
    {
        var config = CreateConfig([]);
        var act = () => config.ResolveEnv("host={!DB_HOST}");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DB_HOST*not set*");
    }

    [Fact]
    public void ResolveEnv_Required_Missing_NonThrowingMode_KeepsLiteralPlaceholder()
    {
        // The serialization/inspection paths (--config output, config validation) must be able to
        // render a configuration whose required variables are not set yet - the placeholder is kept
        // verbatim instead of throwing. Real value access still throws (test above).
        var config = CreateConfig([]);
        config.ResolveEnv("host={!DB_HOST}", throwOnMissingRequired: false)
            .Should().Be("host={!DB_HOST}");
    }

    [Fact]
    public void ResolveEnv_Mixed_OptionalAndRequired()
    {
        var config = CreateConfig(new() { { "REQ", "r" } }); // OPT not set
        // required resolves; unset optional is left untouched
        config.ResolveEnv("{!REQ}/{OPT}").Should().Be("r/{OPT}");
    }

    // ---- GetConfigBool ----

    [Fact]
    public void GetConfigBool_Optional_Missing_ReturnsDefaultFalse()
    {
        var config = CreateConfig([]);
        config.GetConfigBool("Enabled", Section("Enabled", "{GITHUB_AUTH_ENABLED}")).Should().BeFalse();
    }

    [Fact]
    public void GetConfigBool_Optional_Missing_HonoursProvidedDefault()
    {
        var config = CreateConfig([]);
        config.GetConfigBool("Enabled", Section("Enabled", "{GITHUB_AUTH_ENABLED}"), defaultVal: true).Should().BeTrue();
    }

    [Fact]
    public void GetConfigBool_Required_Missing_Throws()
    {
        var config = CreateConfig([]);
        var act = () => config.GetConfigBool("Enabled", Section("Enabled", "{!GITHUB_AUTH_ENABLED}"));
        act.Should().Throw<InvalidOperationException>().WithMessage("*GITHUB_AUTH_ENABLED*not set*");
    }

    [Fact]
    public void GetConfigBool_Resolved_ParsesValue()
    {
        var config = CreateConfig(new() { { "GITHUB_AUTH_ENABLED", "true" } });
        config.GetConfigBool("Enabled", Section("Enabled", "{GITHUB_AUTH_ENABLED}")).Should().BeTrue();
    }

    [Fact]
    public void GetConfigBool_GenuinelyInvalidValue_StillThrows()
    {
        var config = CreateConfig([]);
        var act = () => config.GetConfigBool("Enabled", Section("Enabled", "maybe"));
        act.Should().Throw<InvalidOperationException>().WithMessage("*Invalid boolean value*");
    }

    // ---- GetConfigInt ----

    [Fact]
    public void GetConfigInt_Optional_Missing_ReturnsNull()
    {
        var config = CreateConfig([]);
        config.GetConfigInt("Port", Section("Port", "{SOME_PORT}")).Should().BeNull();
    }

    [Fact]
    public void GetConfigInt_Required_Missing_Throws()
    {
        var config = CreateConfig([]);
        var act = () => config.GetConfigInt("Port", Section("Port", "{!SOME_PORT}"));
        act.Should().Throw<InvalidOperationException>().WithMessage("*SOME_PORT*not set*");
    }

    [Fact]
    public void GetConfigInt_GenuinelyInvalidValue_StillThrows()
    {
        var config = CreateConfig([]);
        var act = () => config.GetConfigInt("Port", Section("Port", "not-a-number"));
        act.Should().Throw<InvalidOperationException>().WithMessage("*Invalid integer value*");
    }

    // ---- GetConfigStr (string type also honours optional/required) ----

    [Fact]
    public void GetConfigStr_Optional_Missing_LeftUntouched()
    {
        var config = CreateConfig([]);
        // strings preserve an unset optional token verbatim (use {!NAME} to require it instead)
        config.GetConfigStr("Name", Section("Name", "{MISSING}")).Should().Be("{MISSING}");
    }

    [Fact]
    public void GetConfigStr_Required_Missing_Throws()
    {
        var config = CreateConfig([]);
        var act = () => config.GetConfigStr("Name", Section("Name", "{!MISSING}"));
        act.Should().Throw<InvalidOperationException>().WithMessage("*MISSING*not set*");
    }
}
