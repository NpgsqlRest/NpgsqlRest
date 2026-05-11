using System.Diagnostics;

namespace NpgsqlRestTests.CliTests;

public class CliCommandTests
{
    private static readonly string DllPath = ResolveDllPath();

    private static string ResolveDllPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NpgsqlRest.sln")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            throw new InvalidOperationException(
                "Could not find NpgsqlRest.sln in parent directories of " + AppContext.BaseDirectory);
        }

        // Find the built DLL matching the current build configuration.
        // AppContext.BaseDirectory is e.g. .../NpgsqlRestTests/bin/Debug/net10.0/
        // Extract the configuration (Debug/Release) from that path.
        var baseParts = AppContext.BaseDirectory.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var binIndex = Array.LastIndexOf(baseParts, "bin");
        var configuration = binIndex >= 0 && binIndex + 1 < baseParts.Length ? baseParts[binIndex + 1] : "Debug";

        var dllPath = Path.Combine(dir.FullName, "NpgsqlRestClient", "bin", configuration, "net10.0", "NpgsqlRestClient.dll");
        if (!File.Exists(dllPath))
        {
            throw new InvalidOperationException("NpgsqlRestClient.dll not found at " + dllPath);
        }
        return dllPath;
    }

    private static async Task<(string Stdout, string Stderr, int ExitCode)> RunCliAsync(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add(DllPath);
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(cts.Token);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return (stdout, stderr, process.ExitCode);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("/?")]
    public async Task Help_WritesUsageToStdout(string flag)
    {
        var (stdout, stderr, exitCode) = await RunCliAsync(flag);

        exitCode.Should().Be(0);
        stdout.Should().Contain("Usage:");
        stdout.Should().Contain("npgsqlrest");
        stderr.Should().BeEmpty();
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    [InlineData("/v")]
    public async Task Version_WritesVersionInfoToStdout(string flag)
    {
        var (stdout, stderr, exitCode) = await RunCliAsync(flag);

        exitCode.Should().Be(0);
        stdout.Should().Contain("Versions:");
        stdout.Should().Contain("NpgsqlRestClient");
        stderr.Should().BeEmpty();
    }

    [Fact]
    public async Task VersionJson_WritesValidJsonToStdout()
    {
        var (stdout, stderr, exitCode) = await RunCliAsync("--version", "--json");

        exitCode.Should().Be(0);
        stdout.TrimStart().Should().StartWith("{");
        var json = JsonNode.Parse(stdout);
        json.Should().NotBeNull();
        json!["versions"].Should().NotBeNull();
        json["versions"]!["NpgsqlRestClient"].Should().NotBeNull();
        stderr.Should().BeEmpty();
    }

    [Fact]
    public async Task Hash_WritesHashedValueToStdout()
    {
        var (stdout, stderr, exitCode) = await RunCliAsync("--hash", "MyPassword123");

        exitCode.Should().Be(0);
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hashLine = lines.Last();
        hashLine.Should().NotBe("MyPassword123");
        hashLine.Should().NotBeEmpty();
        stderr.Should().BeEmpty();
    }

    [Fact]
    public async Task BasicAuth_WritesAuthorizationHeaderToStdout()
    {
        var (stdout, stderr, exitCode) = await RunCliAsync("--basic_auth", "myuser", "mypass");

        exitCode.Should().Be(0);
        var expectedBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("myuser:mypass"));
        stdout.Should().Contain($"Authorization: Basic {expectedBase64}");
        stderr.Should().BeEmpty();
    }

    [Fact]
    public async Task ConfigSchema_WritesJsonSchemaToStdout()
    {
        var (stdout, stderr, exitCode) = await RunCliAsync("--config-schema");

        exitCode.Should().Be(0);
        var json = JsonNode.Parse(stdout);
        json.Should().NotBeNull();
        (json!["$schema"] is not null || json["properties"] is not null).Should().BeTrue();
        // Verify descriptions are present in schema properties
        var appNameProp = json["properties"]?["ApplicationName"];
        appNameProp.Should().NotBeNull();
        appNameProp!["description"].Should().NotBeNull();
        stderr.Should().BeEmpty();
    }

    [Fact]
    public async Task Annotations_WritesJsonArrayToStdout()
    {
        var (stdout, stderr, exitCode) = await RunCliAsync("--annotations");

        exitCode.Should().Be(0);
        var json = JsonNode.Parse(stdout);
        json.Should().NotBeNull();
        json.Should().BeOfType<JsonArray>();
        json!.AsArray().Count.Should().BeGreaterThan(0);
        stderr.Should().BeEmpty();
    }

    [Fact]
    public async Task Config_WithOverride_ShowsUpdatedValue()
    {
        var (stdout, stderr, exitCode) = await RunCliAsync("--ApplicationName=test", "--config");

        exitCode.Should().Be(0);
        var json = ParseJsonc(stdout);
        json.Should().NotBeNull();
        json!["ApplicationName"]?.GetValue<string>().Should().Be("test");
        stderr.Should().BeEmpty();
    }

    [Fact]
    public async Task Config_WithCaseInsensitiveOverride_UpdatesExistingKey()
    {
        var (stdout, stderr, exitCode) = await RunCliAsync("--Applicationname=test", "--config");

        exitCode.Should().Be(0);
        var json = ParseJsonc(stdout);
        json.Should().NotBeNull();
        // Should update the existing "ApplicationName" key (canonical casing), not create a new "Applicationname"
        json!["ApplicationName"]?.GetValue<string>().Should().Be("test");
        json["Applicationname"].Should().BeNull();
        stderr.Should().BeEmpty();
    }

    [Fact]
    public async Task Config_WithUnknownKey_DefaultWarningMode_StillDumpsConfig()
    {
        var (stdout, stderr, exitCode) = await RunCliAsync("--xxx=test", "--config");

        exitCode.Should().Be(0);
        stderr.Should().Contain("Warning");
        stderr.Should().Contain("unknown key");
        stderr.Should().Contain("xxx");
        stdout.TrimStart().Should().StartWith("{");
        var json = ParseJsonc(stdout);
        json.Should().NotBeNull();
    }

    [Fact]
    public async Task Config_WithUnknownKey_ErrorMode_FailsValidation()
    {
        var (stdout, stderr, exitCode) = await RunCliAsync(
            "--Config:ValidateConfigKeys=Error",
            "--xxx=test",
            "--config");

        exitCode.Should().Be(1);
        stderr.Should().Contain("failed");
        stderr.Should().Contain("unknown key");
        stderr.Should().Contain("xxx");
        stdout.Should().BeEmpty();
    }

    [Fact]
    public async Task Config_WithUnknownKey_IgnoreMode_DumpsConfigSilently()
    {
        var (stdout, stderr, exitCode) = await RunCliAsync(
            "--Config:ValidateConfigKeys=Ignore",
            "--xxx=test",
            "--config");

        exitCode.Should().Be(0);
        stderr.Should().BeEmpty();
        stdout.TrimStart().Should().StartWith("{");
    }

    [Fact]
    public async Task Validate_Json_WithUnknownKey_DefaultWarningMode_ConfigStillValid()
    {
        var (stdout, _, _) = await RunCliAsync("--xxx=test", "--validate", "--json");

        var json = JsonNode.Parse(stdout);
        json.Should().NotBeNull();
        json!["configValid"]!.GetValue<bool>().Should().BeTrue();
        json["warningsAreFatal"]!.GetValue<bool>().Should().BeFalse();
        json["validationMode"]!.GetValue<string>().Should().Be("Warning");
        json["warnings"]!.AsArray().Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Validate_Json_WithUnknownKey_ErrorMode_ConfigInvalid()
    {
        var (stdout, _, _) = await RunCliAsync(
            "--Config:ValidateConfigKeys=Error",
            "--xxx=test",
            "--validate",
            "--json");

        var json = JsonNode.Parse(stdout);
        json.Should().NotBeNull();
        json!["configValid"]!.GetValue<bool>().Should().BeFalse();
        json["warningsAreFatal"]!.GetValue<bool>().Should().BeTrue();
        json["validationMode"]!.GetValue<string>().Should().Be("Error");
        json["warnings"]!.AsArray().Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Config_OutputContainsComments()
    {
        var (stdout, stderr, exitCode) = await RunCliAsync("--config");

        exitCode.Should().Be(0);
        // JSONC output should contain // comments
        stdout.Should().Contain("//");
        // Verify it's parseable as JSONC
        var json = ParseJsonc(stdout);
        json.Should().NotBeNull();
        json!.AsObject().ContainsKey("ApplicationName").Should().BeTrue();
        stderr.Should().BeEmpty();
    }

    [Fact]
    public async Task Config_OutputMatchesAppsettingsJson()
    {
        // Run --config from a temp directory (no appsettings.json) to get pure defaults
        var tempDir = Path.Combine(Path.GetTempPath(), $"npgsqlrest_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = tempDir,
            };
            psi.ArgumentList.Add(DllPath);
            psi.ArgumentList.Add("--config");

            using var process = new Process { StartInfo = psi };
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(cts.Token);
            var configOutput = await stdoutTask;

            process.ExitCode.Should().Be(0);

            // Resolve appsettings.json path
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NpgsqlRest.sln")))
            {
                dir = dir.Parent;
            }
            dir.Should().NotBeNull("NpgsqlRest.sln must be found");
            var appsettingsPath = Path.Combine(dir!.FullName, "NpgsqlRestClient", "appsettings.json");
            File.Exists(appsettingsPath).Should().BeTrue($"appsettings.json must exist at {appsettingsPath}");

            var appsettingsContent = await File.ReadAllTextAsync(appsettingsPath);

            // Normalize line endings for comparison
            configOutput = configOutput.Replace("\r\n", "\n").TrimEnd();
            appsettingsContent = appsettingsContent.Replace("\r\n", "\n").TrimEnd();

            configOutput.Should().Be(appsettingsContent, "the --config output (defaults) should be identical to the checked-in appsettings.json");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidArg_WritesErrorToStderr()
    {
        var (stdout, stderr, exitCode) = await RunCliAsync("--invalid-arg");

        exitCode.Should().Be(0);
        stderr.Should().Contain("Unknown parameter --invalid-arg");
        stderr.Should().Contain("--help");
    }

    [Theory]
    [InlineData("--config", "cors")]
    [InlineData("--config=cors")]
    public async Task Config_WithFilter_ShowsFilteredOutput(string arg1, string? arg2 = null)
    {
        var args = arg2 is not null ? new[] { arg1, arg2 } : new[] { arg1 };
        var (stdout, stderr, exitCode) = await RunCliAsync(args);

        exitCode.Should().Be(0);
        // Output is valid JSONC wrapped in { }
        stdout.TrimStart().Should().StartWith("{");
        stdout.TrimEnd().Should().EndWith("}");
        var parsed = ParseJsonc(stdout);
        parsed.Should().NotBeNull("filtered config should be valid JSONC");
        // Cors section preserved with children
        parsed!["Cors"].Should().NotBeNull();
        parsed["Cors"]!["AllowedOrigins"].Should().NotBeNull();
        parsed["Cors"]!["AllowedMethods"].Should().NotBeNull();
        // Should NOT contain unrelated sections
        parsed["ThreadPool"].Should().BeNull();
        parsed["Urls"].Should().BeNull();
    }

    [Fact]
    public async Task Config_WithFilter_PartialSection()
    {
        var (stdout, stderr, exitCode) = await RunCliAsync("--config", "minworker");

        exitCode.Should().Be(0);
        var parsed = ParseJsonc(stdout);
        parsed.Should().NotBeNull();
        // ThreadPool section should exist (as wrapper for matched key)
        var threadPool = parsed!["ThreadPool"]!.AsObject();
        threadPool.Should().NotBeNull();
        threadPool.ContainsKey("MinWorkerThreads").Should().BeTrue();
        // Other ThreadPool keys should NOT be present
        threadPool.ContainsKey("MaxWorkerThreads").Should().BeFalse();
        threadPool.ContainsKey("MinCompletionPortThreads").Should().BeFalse();
    }

    [Fact]
    public async Task Config_WithFilter_NoMatch_ShowsNoResults()
    {
        var (stdout, stderr, exitCode) = await RunCliAsync("--config", "xyz_absolutely_no_match_here");

        exitCode.Should().Be(0);
        stdout.Should().Contain("No results for");
        stdout.Should().Contain("xyz_absolutely_no_match_here");
    }

    private static JsonNode? ParseJsonc(string jsonc)
    {
        return JsonNode.Parse(jsonc, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip
        });
    }
}
