namespace NpgsqlRestClient.Testing;

/// <summary>
/// Per-file header annotations, read from the LEADING line comments of a test file (scanning stops at the
/// first line that is neither blank, a <c>--</c> comment, nor an include):
/// <code>
/// -- @setup StepName [StepName ...]      (run before this file; names from TestRunner:Steps)
/// -- @teardown StepName [StepName ...]   (run after this file, always, best-effort)
/// -- @connection Name                    (run this file on a named ConnectionStrings entry)
/// </code>
/// Annotations are repeatable and names accumulate in written order; multiple names on one line may be
/// separated by whitespace or commas (`@setup A B` == `@setup A, B` — the NpgsqlRest annotation idiom).
/// Other comment lines are ignored, so ordinary file documentation can freely surround the annotations.
/// An include (<c>\i</c>/<c>\ir</c>) in the header region is scanned AS IF PASTED: annotations in the
/// included file's own leading comments count, and the header ends where the pasted content's first
/// statement would be — so a shared "profile" file of annotations can be attached with one include line.
/// </summary>
public sealed class TestFileHeader
{
    public List<string> Setup { get; } = [];
    public List<string> Teardown { get; } = [];
    public string? ConnectionName { get; private set; }

    public static TestFileHeader Parse(string content, string? sourceFile = null)
    {
        var header = new TestFileHeader();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        if (sourceFile is not null)
        {
            visited.Add(Path.GetFullPath(sourceFile));
        }
        Scan(content, sourceFile, header, visited, depth: 0);
        return header;
    }

    // Scans the leading header region; returns false when a statement ended the header (so an including
    // file must stop scanning too — paste semantics), true when the whole content was header material.
    private static bool Scan(string content, string? sourceFile, TestFileHeader header, HashSet<string> visited, int depth)
    {
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("--"))
            {
                var comment = line[2..].TrimStart();
                if (!comment.StartsWith('@')) continue;

                // Names are separated by whitespace and/or commas (CSV works: `@setup A, B` == `@setup A B`).
                var tokens = comment[1..].Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (tokens.Length < 2) continue;

                if (string.Equals(tokens[0], "setup", StringComparison.OrdinalIgnoreCase))
                {
                    header.Setup.AddRange(tokens[1..]);
                }
                else if (string.Equals(tokens[0], "teardown", StringComparison.OrdinalIgnoreCase))
                {
                    header.Teardown.AddRange(tokens[1..]);
                }
                else if (string.Equals(tokens[0], "connection", StringComparison.OrdinalIgnoreCase))
                {
                    header.ConnectionName = tokens[1];
                }
                // any other @word is an ordinary comment — ignored
                continue;
            }

            if (SqlTestFileParser.TryParseIncludeLine(line, out var incPath, out var relative))
            {
                if (depth >= 16)
                {
                    throw new InvalidOperationException($"include nesting deeper than 16 at '{incPath}' while reading header annotations");
                }
                var baseDir = relative && sourceFile is not null
                    ? Path.GetDirectoryName(Path.GetFullPath(sourceFile))!
                    : Environment.CurrentDirectory;
                var fullPath = Path.GetFullPath(incPath, baseDir);
                if (!File.Exists(fullPath))
                {
                    throw new InvalidOperationException($"include not found: '{incPath}' resolved to '{fullPath}' while reading header annotations");
                }
                if (!visited.Add(fullPath))
                {
                    throw new InvalidOperationException($"circular include: '{fullPath}' while reading header annotations");
                }
                var keepScanning = Scan(File.ReadAllText(fullPath), fullPath, header, visited, depth + 1);
                visited.Remove(fullPath);
                if (!keepScanning)
                {
                    return false; // the pasted content contained a statement — the header ends there
                }
                continue; // comments/annotations-only include — the header continues in this file
            }

            return false; // first statement (or HTTP block start) ends the header
        }
        return true;
    }
}

/// <summary>
/// Loads parsed steps with <c>\i</c>/<c>\ir</c> includes expanded in place — paste semantics: each include
/// line is replaced by the included file's parsed steps (SQL statements and HTTP blocks alike), recursively
/// (cycle-safe, depth-capped). Included steps carry <see cref="TestStep.SourceFile"/> so failures are
/// attributed to the included file and line.
/// </summary>
public static class TestFileLoader
{
    private const int MaxIncludeDepth = 16;

    /// <summary>
    /// Parses <paramref name="content"/> and expands include steps. <paramref name="sourceFile"/> is the
    /// full path of the file the content came from (used to resolve <c>\ir</c> and for attribution); null
    /// for inline SQL, in which case <c>\ir</c> falls back to cwd-relative like <c>\i</c>.
    /// </summary>
    public static List<TestStep> LoadSteps(string content, string? sourceFile)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        if (sourceFile is not null)
        {
            visited.Add(Path.GetFullPath(sourceFile));
        }
        return Expand(SqlTestFileParser.Parse(content), sourceFile, isIncluded: false, visited, depth: 0);
    }

    private static List<TestStep> Expand(List<TestStep> steps, string? sourceFile, bool isIncluded, HashSet<string> visited, int depth)
    {
        var result = new List<TestStep>(steps.Count);
        foreach (var step in steps)
        {
            if (step is not IncludeStep inc)
            {
                if (isIncluded)
                {
                    step.SourceFile = sourceFile;
                }
                result.Add(step);
                continue;
            }

            var from = isIncluded ? sourceFile : null;
            var baseDir = inc.RelativeToFile && sourceFile is not null
                ? Path.GetDirectoryName(Path.GetFullPath(sourceFile))!
                : Environment.CurrentDirectory;
            var fullPath = Path.GetFullPath(inc.Path, baseDir);

            if (depth >= MaxIncludeDepth)
            {
                throw new InvalidOperationException($"include nesting deeper than {MaxIncludeDepth} at '{inc.Path}' (line {inc.LineNumber}{(from is null ? "" : $" of {from}")})");
            }
            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException($"include not found: '{inc.Path}' resolved to '{fullPath}' (line {inc.LineNumber}{(from is null ? "" : $" of {from}")})");
            }
            if (!visited.Add(fullPath))
            {
                throw new InvalidOperationException($"circular include: '{fullPath}' (line {inc.LineNumber}{(from is null ? "" : $" of {from}")})");
            }

            var inner = SqlTestFileParser.Parse(File.ReadAllText(fullPath));
            result.AddRange(Expand(inner, fullPath, isIncluded: true, visited, depth + 1));
            visited.Remove(fullPath);
        }
        return result;
    }
}
