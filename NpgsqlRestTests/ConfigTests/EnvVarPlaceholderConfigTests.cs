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
/// <item><c>{!NAME:fallback}</c> - resolves to the variable's value when set, otherwise to the literal
/// fallback text - never throws. Only the bang form takes a fallback: a plain <c>{NAME:...}</c> stays
/// literal so brace-colon syntax (Serilog format specifiers, CSS, TS types) is never consumed.</item>
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

    // ---- {!NAME:fallback} ----

    [Fact]
    public void ResolveEnv_Fallback_Present_UsesValue()
    {
        var config = CreateConfig(new() { { "FB_DB_HOST", "db.internal" } });
        config.ResolveEnv("host={!FB_DB_HOST:localhost}").Should().Be("host=db.internal");
    }

    [Fact]
    public void ResolveEnv_Fallback_Missing_UsesFallback()
    {
        var config = CreateConfig([]);
        config.ResolveEnv("host={!FB_DB_HOST:localhost}").Should().Be("host=localhost");
    }

    [Fact]
    public void ResolveEnv_Fallback_Missing_NonThrowingMode_AlsoUsesFallback()
    {
        // The fallback IS the value the running application will use, so the serialization/inspection
        // paths (--config output, config validation) resolve it too instead of keeping the literal.
        var config = CreateConfig([]);
        config.ResolveEnv("host={!FB_DB_HOST:localhost}", throwOnMissingRequired: false)
            .Should().Be("host=localhost");
    }

    [Fact]
    public void ResolveEnv_Fallback_Empty_ResolvesToEmptyString()
    {
        var config = CreateConfig([]);
        config.ResolveEnv("opts=[{!FB_OPTS:}]").Should().Be("opts=[]");
    }

    [Fact]
    public void ResolveEnv_Fallback_MayContainColons()
    {
        // the fallback starts after the FIRST ':' and runs to the closing brace
        var config = CreateConfig([]);
        config.ResolveEnv("url={!FB_URL:http://localhost:5000}").Should().Be("url=http://localhost:5000");
    }

    [Fact]
    public void ResolveEnv_PlainColonToken_NeverTreatedAsFallback()
    {
        // brace-colon content without the bang stays literal - Serilog templates, CSS, TS types
        var config = CreateConfig([]);
        config.ResolveEnv("[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}")
            .Should().Be("[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}");
        config.ResolveEnv("td{border:1px solid #d4d4d4;padding:4px 8px}")
            .Should().Be("td{border:1px solid #d4d4d4;padding:4px 8px}");
        config.ResolveEnv("{status: number; title: string} | undefined")
            .Should().Be("{status: number; title: string} | undefined");
    }

    [Fact]
    public void ResolveEnv_DefaultConnectionString_AllFallbacks_NoEnvironment()
    {
        // the shipped default connection string shape: fallbacks fill everything except the
        // required {!PGDATABASE}, which still fails fast naming the variable
        var config = CreateConfig([]);
        config.ResolveEnv("Host={!FB_PGHOST:localhost};Port={!FB_PGPORT:5432};Username={!FB_PGUSER:postgres};Password={!FB_PGPASSWORD:postgres}")
            .Should().Be("Host=localhost;Port=5432;Username=postgres;Password=postgres");
        var act = () => config.ResolveEnv("Database={!FB_PGDATABASE}");
        act.Should().Throw<InvalidOperationException>().WithMessage("*FB_PGDATABASE*not set*");
    }

    [Fact]
    public void GetConfigBool_Fallback_ParsesFallbackValue()
    {
        var config = CreateConfig([]);
        config.GetConfigBool("Enabled", Section("Enabled", "{!FB_FLAG:true}")).Should().BeTrue();
    }

    [Fact]
    public void GetConfigInt_Fallback_ParsesFallbackValue()
    {
        var config = CreateConfig([]);
        config.GetConfigInt("Port", Section("Port", "{!FB_PORT:5432}")).Should().Be(5432);
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
