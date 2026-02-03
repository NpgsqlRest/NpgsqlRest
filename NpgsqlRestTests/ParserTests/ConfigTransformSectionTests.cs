using Microsoft.Extensions.Configuration;
using NpgsqlRestClient;

namespace NpgsqlRestTests.ParserTests;

public class ConfigTransformSectionTests
{
    private Config CreateConfigWithEnvDict(Dictionary<string, string> envVars)
    {
        var config = new Config();

        // Build config with ParseEnvironmentVariables enabled
        // We need to set environment variables before building
        foreach (var kvp in envVars)
        {
            Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
        }

        // Create a minimal config that enables ParseEnvironmentVariables
        var json = """
        {
            "Config": {
                "ParseEnvironmentVariables": true
            }
        }
        """;

        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, json);

        try
        {
            config.Build([tempFile], []);
        }
        finally
        {
            File.Delete(tempFile);
            // Clean up environment variables
            foreach (var kvp in envVars)
            {
                Environment.SetEnvironmentVariable(kvp.Key, null);
            }
        }

        return config;
    }

    [Fact]
    public void TransformSection_SimpleReplacement_ReplacesCorrectly()
    {
        // Arrange
        Environment.SetEnvironmentVariable("TEST_HOST", "localhost");
        Environment.SetEnvironmentVariable("TEST_PORT", "9000");

        try
        {
            var config = CreateConfigWithEnvDict(new Dictionary<string, string>
            {
                { "TEST_HOST", "localhost" },
                { "TEST_PORT", "9000" }
            });

            var jsonConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "Endpoints:Http:Url", "http://{TEST_HOST}:{TEST_PORT}" }
                })
                .Build();

            var section = jsonConfig.GetSection("Endpoints");

            // Act
            var transformed = config.TransformSection(section);

            // Assert
            transformed.GetSection("Http:Url").Value.Should().Be("http://localhost:9000");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_HOST", null);
            Environment.SetEnvironmentVariable("TEST_PORT", null);
        }
    }

    [Fact]
    public void TransformSection_NestedSection_TransformsAllLevels()
    {
        // Arrange
        Environment.SetEnvironmentVariable("CERT_PATH", "/certs/server.pfx");
        Environment.SetEnvironmentVariable("CERT_PASS", "secret123");

        try
        {
            var config = CreateConfigWithEnvDict(new Dictionary<string, string>
            {
                { "CERT_PATH", "/certs/server.pfx" },
                { "CERT_PASS", "secret123" }
            });

            var jsonConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "Endpoints:Https:Url", "https://example.com:5001" },
                    { "Endpoints:Https:Certificate:Path", "{CERT_PATH}" },
                    { "Endpoints:Https:Certificate:Password", "{CERT_PASS}" }
                })
                .Build();

            var section = jsonConfig.GetSection("Endpoints");

            // Act
            var transformed = config.TransformSection(section);

            // Assert
            transformed.GetSection("Https:Url").Value.Should().Be("https://example.com:5001");
            transformed.GetSection("Https:Certificate:Path").Value.Should().Be("/certs/server.pfx");
            transformed.GetSection("Https:Certificate:Password").Value.Should().Be("secret123");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CERT_PATH", null);
            Environment.SetEnvironmentVariable("CERT_PASS", null);
        }
    }

    [Fact]
    public void TransformSection_NoEnvDict_ReturnsOriginalSection()
    {
        // Arrange - create config without ParseEnvironmentVariables
        var json = """
        {
            "Config": {
                "ParseEnvironmentVariables": false
            }
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

        var jsonConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Key", "{NOT_REPLACED}" }
            })
            .Build();

        var section = jsonConfig.GetSection("Key");

        // Act
        var transformed = config.TransformSection(section);

        // Assert - should return original section unchanged
        transformed.Should().BeSameAs(section);
    }

    [Fact]
    public void TransformSection_UnknownEnvVar_KeepsPlaceholder()
    {
        // Arrange
        var config = CreateConfigWithEnvDict(new Dictionary<string, string>
        {
            { "KNOWN_VAR", "known_value" }
        });

        var jsonConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Test:Known", "{KNOWN_VAR}" },
                { "Test:Unknown", "{UNKNOWN_VAR}" }
            })
            .Build();

        var section = jsonConfig.GetSection("Test");

        // Act
        var transformed = config.TransformSection(section);

        // Assert
        transformed.GetSection("Known").Value.Should().Be("known_value");
        transformed.GetSection("Unknown").Value.Should().Be("{UNKNOWN_VAR}");
    }

    [Fact]
    public void TransformSection_MixedContent_TransformsOnlyPlaceholders()
    {
        // Arrange
        var config = CreateConfigWithEnvDict(new Dictionary<string, string>
        {
            { "MAX_SIZE", "50000000" }
        });

        var jsonConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Limits:MaxRequestBodySize", "{MAX_SIZE}" },
                { "Limits:MaxConnections", "100" }
            })
            .Build();

        var section = jsonConfig.GetSection("Limits");

        // Act
        var transformed = config.TransformSection(section);

        // Assert
        transformed.GetSection("MaxRequestBodySize").Value.Should().Be("50000000");
        transformed.GetSection("MaxConnections").Value.Should().Be("100");
    }

    [Fact]
    public void TransformSection_EmptySection_ReturnsEmptyConfig()
    {
        // Arrange
        var config = CreateConfigWithEnvDict(new Dictionary<string, string>
        {
            { "VAR", "value" }
        });

        var jsonConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var section = jsonConfig.GetSection("NonExistent");

        // Act
        var transformed = config.TransformSection(section);

        // Assert
        transformed.GetChildren().Should().BeEmpty();
    }
}
