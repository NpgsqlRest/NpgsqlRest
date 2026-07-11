using NpgsqlRestClient;

namespace NpgsqlRestTests.ConfigTests;

/// <summary>
/// The <c>Config:EnvFile</c> loader (default <c>"./.env"</c> since 3.20.1):
/// <list type="bullet">
/// <item>Variables already present in the process environment always win - the file only fills in
/// missing ones (dotenv convention; before 3.20.1 the file overwrote the environment).</item>
/// <item>Within the file a repeated key keeps its last value.</item>
/// <item>The load outcome is recorded on <see cref="Config.EnvFileState"/> (with path and
/// loaded/skipped counts) and logged later by Program: the missing shipped default is informational,
/// a missing custom path is a warning.</item>
/// </list>
/// </summary>
public class EnvFileTests
{
    // Builds a Config from a temp config file with the given EnvFile value ("null" literal for null).
    private static Config CreateConfig(string envFileJsonValue)
    {
        var config = new Config();
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, $$"""{ "Config": { "ParseEnvironmentVariables": true, "EnvFile": {{envFileJsonValue}} } }""");
        try
        {
            config.Build([tempFile], []);
        }
        finally
        {
            File.Delete(tempFile);
        }
        return config;
    }

    private static string WriteEnvFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"npgsqlrest_envfile_{Guid.NewGuid():N}.env");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void EnvFile_LoadsMissingVariables_IntoEnvDict()
    {
        const string name = "NPGSQLREST_TEST_ENVFILE_NEW";
        Environment.SetEnvironmentVariable(name, null); // ensure absent
        var envFile = WriteEnvFile($"{name}=from_file\n");
        try
        {
            var config = CreateConfig($"\"{envFile}\"");
            config.EnvFileState.Should().Be(Config.EnvFileStatus.Loaded);
            config.EnvFileLoadedCount.Should().Be(1);
            config.EnvFileSkippedCount.Should().Be(0);
            // the loaded variable participates in {NAME} placeholder resolution
            config.ResolveEnv($"v={{{name}}}").Should().Be("v=from_file");
        }
        finally
        {
            File.Delete(envFile);
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void EnvFile_NeverOverridesRealEnvironment()
    {
        const string name = "NPGSQLREST_TEST_ENVFILE_PRESET";
        Environment.SetEnvironmentVariable(name, "from_env");
        var envFile = WriteEnvFile($"{name}=from_file\n");
        try
        {
            var config = CreateConfig($"\"{envFile}\"");
            config.EnvFileState.Should().Be(Config.EnvFileStatus.Loaded);
            config.EnvFileLoadedCount.Should().Be(0);
            config.EnvFileSkippedCount.Should().Be(1);
            Environment.GetEnvironmentVariable(name).Should().Be("from_env");
            config.ResolveEnv($"v={{{name}}}").Should().Be("v=from_env");
        }
        finally
        {
            File.Delete(envFile);
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void EnvFile_DuplicateKeyInFile_LastWins()
    {
        const string name = "NPGSQLREST_TEST_ENVFILE_DUP";
        Environment.SetEnvironmentVariable(name, null); // ensure absent
        var envFile = WriteEnvFile($"{name}=first\n{name}=second\n");
        try
        {
            var config = CreateConfig($"\"{envFile}\"");
            config.EnvFileLoadedCount.Should().Be(1); // one distinct variable
            Environment.GetEnvironmentVariable(name).Should().Be("second");
        }
        finally
        {
            File.Delete(envFile);
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void EnvFile_QuotedValues_AreUnquoted()
    {
        const string name = "NPGSQLREST_TEST_ENVFILE_QUOTED";
        Environment.SetEnvironmentVariable(name, null); // ensure absent
        var envFile = WriteEnvFile($"{name}=\"quoted value\"\n");
        try
        {
            CreateConfig($"\"{envFile}\"");
            Environment.GetEnvironmentVariable(name).Should().Be("quoted value");
        }
        finally
        {
            File.Delete(envFile);
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void EnvFile_MissingCustomPath_IsMissingCustom()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"npgsqlrest_envfile_{Guid.NewGuid():N}.env");
        var config = CreateConfig($"\"{missing}\"");
        config.EnvFileState.Should().Be(Config.EnvFileStatus.MissingCustom); // → logged as a warning
        config.EnvFilePath.Should().Be(missing);
    }

    [Fact]
    public void EnvFile_MissingShippedDefault_IsMissingDefault()
    {
        // the test working directory has no ./.env - the shipped default is optional (informational)
        File.Exists(Path.GetFullPath("./.env")).Should().BeFalse("the test run directory must not contain a .env for this test");
        var config = CreateConfig("\"./.env\"");
        config.EnvFileState.Should().Be(Config.EnvFileStatus.MissingDefault); // → logged as information
    }

    [Fact]
    public void EnvFile_Null_IsNotConfigured()
    {
        var config = CreateConfig("null");
        config.EnvFileState.Should().Be(Config.EnvFileStatus.NotConfigured);
        config.EnvFilePath.Should().BeNull();
    }
}
