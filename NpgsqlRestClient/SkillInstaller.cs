namespace NpgsqlRestClient;

/// <summary>
/// Implements the <c>--install-skill</c> command: downloads the three Claude Code skill files
/// (SKILL.md, annotations-reference.md, configuration-reference.jsonc) from the version branch
/// matching the running binary - falling back to master when that branch is not available - into
/// <c>./.claude/skills/npgsqlrest</c> (project scope, the default) or
/// <c>~/.claude/skills/npgsqlrest</c> (global scope).
/// </summary>
public static class SkillInstaller
{
    private static readonly string[] SkillFiles = ["SKILL.md", "annotations-reference.md", "configuration-reference.jsonc"];

    /// <summary>Path of the skill folder inside the repository (same in every ref).</summary>
    private const string RepoPath = ".claude/skills/npgsqlrest";

    /// <summary>
    /// Base URL up to (excluding) the git ref. Overridable via the NPGSQLREST_SKILL_BASE_URL
    /// environment variable for tests and enterprise mirrors exposing the same
    /// <c>{base}/{ref}/.claude/skills/npgsqlrest/{file}</c> layout.
    /// </summary>
    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("NPGSQLREST_SKILL_BASE_URL")?.TrimEnd('/')
        ?? "https://raw.githubusercontent.com/NpgsqlRest/NpgsqlRest";

    /// <summary>The running version as the release branch name, e.g. "3.20.1".</summary>
    public static string VersionBranch
    {
        get
        {
            var v = System.Reflection.Assembly.GetAssembly(typeof(SkillInstaller))?.GetName().Version;
            return v is null ? "master" : $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
        }
    }

    public static async Task<int> InstallAsync(bool global)
    {
        var targetRoot = global
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Directory.GetCurrentDirectory();
        var targetDir = Path.Combine(targetRoot, ".claude", "skills", "npgsqlrest");
        var version = VersionBranch;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"npgsqlrest/{version}");

        // All files must come from the SAME ref for a consistent skill: try the branch matching
        // this version first, then master. Files are downloaded fully into memory before anything
        // is written, so a network failure never leaves a partial install behind.
        (string GitRef, bool IsFallback)[] refs = string.Equals(version, "master", StringComparison.Ordinal)
            ? [("master", false)]
            : [(version, false), ("master", true)];
        foreach (var (gitRef, isFallback) in refs)
        {
            var contents = new List<(string Name, string Content)>(SkillFiles.Length);
            string? failure = null;
            foreach (var file in SkillFiles)
            {
                var url = $"{BaseUrl}/{gitRef}/{RepoPath}/{file}";
                try
                {
                    using var response = await http.GetAsync(url);
                    if (response.IsSuccessStatusCode is false)
                    {
                        failure = $"{url}: {(int)response.StatusCode} {response.ReasonPhrase}";
                        break;
                    }
                    contents.Add((file, await response.Content.ReadAsStringAsync()));
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    failure = $"{url}: {ex.Message}";
                    break;
                }
            }

            if (failure is not null)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Error.WriteLine(isFallback
                    ? $"Could not download the skill from 'master': {failure}"
                    : $"Skill files for version branch '{gitRef}' are not available ({failure}); trying 'master'...");
                Console.ResetColor();
                continue;
            }

            Directory.CreateDirectory(targetDir);
            foreach (var (name, content) in contents)
            {
                File.WriteAllText(Path.Combine(targetDir, name), content);
                Console.WriteLine($"  {name}");
            }
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Installed the NpgsqlRest Claude Code skill ({gitRef}) into {targetDir}");
            Console.ResetColor();
            if (isFallback)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Note: installed from 'master' because the '{version}' branch was not available - references may not exactly match this version. The skill instructs the agent to trust the installed binary (--config, --annotations) when they differ.");
                Console.ResetColor();
            }
            if (global is false)
            {
                Console.WriteLine("Commit the .claude/skills/npgsqlrest folder so the whole team (and CI agents) get the skill.");
            }
            return 0;
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine("Failed to install the Claude Code skill. Check network access to raw.githubusercontent.com and try again.");
        Console.ResetColor();
        return 1;
    }
}
