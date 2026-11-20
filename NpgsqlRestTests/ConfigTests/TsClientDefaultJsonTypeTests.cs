using NpgsqlRest.TsClient;
using NpgsqlRestClient;

namespace NpgsqlRestTests.ConfigTests;

/// <summary>
/// Regression: with ClientCodeGen:DefaultJsonType not set, the npgsqlrest client used "string" while
/// appsettings.json, the configuration reference and the plugin's own option default all say "any". A
/// configuration without the key therefore typed json and jsonb values as <c>string</c> in the generated
/// TypeScript (multi-command SQL file results, fields of composite types), which is wrong - the value is JSON.
/// The three places must agree.
/// </summary>
public class TsClientDefaultJsonTypeTests
{
    [Fact]
    public void Client_fallback_matches_the_plugin_option_default()
    {
        App.TsClientDefaultJsonType.Should().Be(new TsClientOptions().DefaultJsonType);
    }

    [Fact]
    public void Client_fallback_matches_the_documented_configuration_default()
    {
        var documented = ConfigDefaults.GetDefaults()["NpgsqlRest"]!["ClientCodeGen"]!["DefaultJsonType"]!.GetValue<string>();
        App.TsClientDefaultJsonType.Should().Be(documented);
        documented.Should().Be("any");
    }
}
