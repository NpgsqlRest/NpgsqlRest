using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace NpgsqlRestTests.CliTests;

/// <summary>
/// The <c>--install-skill [global]</c> command. A local Kestrel server stands in for
/// raw.githubusercontent.com via the NPGSQLREST_SKILL_BASE_URL override (same
/// <c>{base}/{ref}/.claude/skills/npgsqlrest/{file}</c> layout). Covers: project scope from the
/// version branch, master fallback when the version branch is absent, global scope honoring the
/// user profile, total failure (exit 1, no partial files), and scope-argument validation.
/// </summary>
public class InstallSkillTests
{
    private static readonly string DllPath = ResolveDllPath();
    private static readonly string[] SkillFiles = ["SKILL.md", "annotations-reference.md", "configuration-reference.jsonc"];

    private static string ResolveDllPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NpgsqlRest.sln")))
        {
            dir = dir.Parent!;
        }
        var baseParts = AppContext.BaseDirectory.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var binIndex = Array.LastIndexOf(baseParts, "bin");
        var configuration = binIndex >= 0 && binIndex + 1 < baseParts.Length ? baseParts[binIndex + 1] : "Debug";
        return Path.Combine(dir!.FullName, "NpgsqlRestClient", "bin", configuration, "net10.0", "NpgsqlRestClient.dll");
    }

    // The version branch the client will request, derived from the built DLL ("3.20.1").
    private static string ExpectedVersionBranch()
    {
        var v = System.Reflection.AssemblyName.GetAssemblyName(DllPath).Version!;
        return $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
    }

    // Serves {ref}/.claude/skills/npgsqlrest/{file} with body "{file}@{ref}" for allowed refs, 404 otherwise.
    private static async Task<WebApplication> StartSkillServerAsync(Func<string, bool> refAvailable)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.MapGet("/{gitRef}/.claude/skills/npgsqlrest/{file}", (string gitRef, string file) =>
            refAvailable(gitRef) && SkillFiles.Contains(file)
                ? Results.Text($"{file}@{gitRef}")
                : Results.NotFound());
        await app.StartAsync();
        return app;
    }

    private static async Task<(string Stdout, string Stderr, int ExitCode)> RunAsync(
        string workDir, Dictionary<string, string> env, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workDir,
        };
        psi.ArgumentList.Add(DllPath);
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }
        foreach (var (key, value) in env)
        {
            psi.Environment[key] = value;
        }

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(cts.Token);
        return (await stdoutTask, await stderrTask, process.ExitCode);
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"npgsqlrest_skill_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task InstallSkill_ProjectScope_InstallsFromVersionBranch()
    {
        var app = await StartSkillServerAsync(_ => true); // every ref available → version branch wins
        var workDir = TempDir();
        try
        {
            var (stdout, _, exitCode) = await RunAsync(workDir,
                new() { ["NPGSQLREST_SKILL_BASE_URL"] = app.Urls.First() }, "--install-skill");

            exitCode.Should().Be(0, stdout);
            var version = ExpectedVersionBranch();
            var skillDir = Path.Combine(workDir, ".claude", "skills", "npgsqlrest");
            foreach (var file in SkillFiles)
            {
                File.ReadAllText(Path.Combine(skillDir, file)).Should().Be($"{file}@{version}");
            }
            stdout.Should().Contain($"({version})").And.Contain(skillDir);
        }
        finally
        {
            await app.DisposeAsync();
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public async Task InstallSkill_VersionBranchMissing_FallsBackToMaster()
    {
        var app = await StartSkillServerAsync(gitRef => gitRef == "master");
        var workDir = TempDir();
        try
        {
            var (stdout, stderr, exitCode) = await RunAsync(workDir,
                new() { ["NPGSQLREST_SKILL_BASE_URL"] = app.Urls.First() }, "--install-skill");

            exitCode.Should().Be(0, stdout);
            stderr.Should().Contain("trying 'master'");
            stdout.Should().Contain("Note: installed from 'master'");
            var skillDir = Path.Combine(workDir, ".claude", "skills", "npgsqlrest");
            File.ReadAllText(Path.Combine(skillDir, "SKILL.md")).Should().Be("SKILL.md@master");
        }
        finally
        {
            await app.DisposeAsync();
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public async Task InstallSkill_GlobalScope_InstallsIntoUserProfile()
    {
        var app = await StartSkillServerAsync(_ => true);
        var workDir = TempDir();
        var fakeHome = TempDir();
        try
        {
            // SpecialFolder.UserProfile resolves from HOME (unix) / USERPROFILE (windows)
            var (stdout, _, exitCode) = await RunAsync(workDir,
                new()
                {
                    ["NPGSQLREST_SKILL_BASE_URL"] = app.Urls.First(),
                    ["HOME"] = fakeHome,
                    ["USERPROFILE"] = fakeHome,
                }, "--install-skill", "global");

            exitCode.Should().Be(0, stdout);
            var skillDir = Path.Combine(fakeHome, ".claude", "skills", "npgsqlrest");
            File.Exists(Path.Combine(skillDir, "SKILL.md")).Should().BeTrue(stdout);
            // nothing installed into the working directory in global scope
            Directory.Exists(Path.Combine(workDir, ".claude")).Should().BeFalse();
        }
        finally
        {
            await app.DisposeAsync();
            Directory.Delete(workDir, recursive: true);
            Directory.Delete(fakeHome, recursive: true);
        }
    }

    [Fact]
    public async Task InstallSkill_ServerUnreachable_FailsWithoutPartialFiles()
    {
        var workDir = TempDir();
        try
        {
            // port 1 - connection refused immediately, for both the version branch and master attempts
            var (stdout, stderr, exitCode) = await RunAsync(workDir,
                new() { ["NPGSQLREST_SKILL_BASE_URL"] = "http://127.0.0.1:1" }, "--install-skill");

            exitCode.Should().Be(1, stdout);
            stderr.Should().Contain("Failed to install");
            Directory.Exists(Path.Combine(workDir, ".claude")).Should().BeFalse("a failed install must not leave partial files");
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public async Task InstallSkill_UnknownScope_FailsWithUsageError()
    {
        var workDir = TempDir();
        try
        {
            var (_, stderr, exitCode) = await RunAsync(workDir, [], "--install-skill", "everywhere");
            exitCode.Should().Be(1);
            stderr.Should().Contain("Unknown --install-skill scope 'everywhere'");
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }
}
