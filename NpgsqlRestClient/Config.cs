using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NpgsqlRest;

namespace NpgsqlRestClient;

public class Config
{
    public IConfigurationRoot Cfg { get; private set; } = null!;
    public IConfigurationSection NpgsqlRestCfg { get; private set; } = null!;
    public IConfigurationSection ConnectionSettingsCfg { get; private set; } = null!;
    public bool UseJsonApplicationName { get; private set; }
    public string CurrentDir => Directory.GetCurrentDirectory();
    public Dictionary<string, string>? EnvDict { get; private set; } = null;
    public string? ConfigFilter { get; private set; }
    
    public void Build(string[] args, string[] skip)
    {
        if (args.Length >= 1 && skip.Contains(args[0], StringComparer.CurrentCultureIgnoreCase))
        {
            args = [];
        }
        
        var tempBuilder = new ConfigurationBuilder();
        IConfigurationRoot tempCfg;

        var arguments = new Out();
        var (configFiles, commandLineArgs) = BuildFromArgs(args);
        
        if (configFiles.Count > 0)
        {
            foreach (var (fileName, optional) in configFiles)
            {
                tempBuilder.AddJsonFile(Path.GetFullPath(fileName, CurrentDir), optional: optional);
            }
            tempCfg = tempBuilder.Build();
        }
        else
        {
            tempCfg = tempBuilder
                .AddJsonFile(Path.GetFullPath("appsettings.json", CurrentDir), optional: true)
                .AddJsonFile(Path.GetFullPath("appsettings.Development.json", CurrentDir), optional: true)
                .Build();
        }

        var cfgCfg = tempCfg.GetSection("Config");
        ConfigurationBuilder configBuilder = new();
        var useEnv = cfgCfg != null && GetConfigBool("AddEnvironmentVariables", cfgCfg);

        if (configFiles.Count > 0)
        {
            foreach (var (fileName, optional) in configFiles)
            {
                configBuilder.AddJsonFile(Path.GetFullPath(fileName, CurrentDir), optional: optional);
            }
            if (useEnv)
            {
                configBuilder.AddEnvironmentVariables();
            }
            configBuilder.AddCommandLine(commandLineArgs);
            Cfg = configBuilder.Build();
        }
        else
        {
            if (useEnv)
            {
                Cfg = configBuilder
                    .AddJsonFile(Path.GetFullPath("appsettings.json", CurrentDir), optional: true)
                    .AddJsonFile(Path.GetFullPath("appsettings.Development.json", CurrentDir), optional: true)
                    .AddEnvironmentVariables()
                    .AddCommandLine(commandLineArgs)
                    .Build();
            }
            else
            {
                Cfg = configBuilder
                    .AddJsonFile(Path.GetFullPath("appsettings.json", CurrentDir), optional: true)
                    .AddJsonFile(Path.GetFullPath("appsettings.Development.json", CurrentDir), optional: true)
                    .AddCommandLine(commandLineArgs)
                    .Build();
            }
        }

        NpgsqlRestCfg = Cfg.GetSection("NpgsqlRest");
        ConnectionSettingsCfg = Cfg.GetSection("ConnectionSettings");
        
        var parseEnv = cfgCfg != null && GetConfigBool("ParseEnvironmentVariables", cfgCfg, true) is true;
        if (useEnv || parseEnv)
        {
            var envFilePath = cfgCfg?.GetSection("EnvFile")?.Value;
            if (envFilePath is not null)
            {
                var fullPath = Path.GetFullPath(envFilePath, CurrentDir);
                if (File.Exists(fullPath))
                {
                    LoadEnvFile(fullPath);
                }
            }
        }

        if (parseEnv)
        {
            EnvDict = new Dictionary<string, string>();
            var envVars = Environment.GetEnvironmentVariables();
            foreach (var key in envVars.Keys)
            {
                EnvDict.Add(key.ToString()!, envVars[key.ToString()!]?.ToString()!);
            }
        }

        UseJsonApplicationName = GetConfigBool("UseJsonApplicationName", ConnectionSettingsCfg);
    }

    public bool Exists(IConfigurationSection? section)
    {
        if (section is null)
        {
            return false;
        }
        if (section.GetChildren().Any() is false) 
        {
            return false;
        }
        return true;
    }

    public bool GetConfigBool(string key, IConfiguration? subsection = null, bool defaultVal = false)
    {
        var section = subsection?.GetSection(key) ?? Cfg.GetSection(key);
        if (string.IsNullOrEmpty(section.Value))
        {
            return defaultVal;
        }

        var value = EnvDict is not null ? 
            Formatter.FormatString(section.Value.AsSpan(), EnvDict).ToString() : 
            section.Value;

        // Handle various boolean representations
        return value.ToLowerInvariant() switch
        {
            "true" or "yes" or "1" => true,
            "false" or "no" or "0" => false,
            _ => throw new InvalidOperationException($"Invalid boolean value '{value}' for configuration key '{key}'. Valid values are: true, false, yes, no, 1, 0")
        };
    }

    public string? GetConfigStr(string key, IConfiguration? subsection = null)
    {
        var section = subsection?.GetSection(key) ?? Cfg.GetSection(key);
        if (string.IsNullOrEmpty(section.Value))
        {
            return null;
        }
        return EnvDict is not null ? 
            Formatter.FormatString(section.Value.AsSpan(), EnvDict).ToString() : 
            section.Value;
    }

    public int? GetConfigInt(string key, IConfiguration? subsection = null)
    {
        var section = subsection?.GetSection(key) ?? Cfg.GetSection(key);
        if (string.IsNullOrEmpty(section.Value))
        {
            return null;
        }
        var configValue = EnvDict is not null ? 
            Formatter.FormatString(section.Value.AsSpan(), EnvDict).ToString() : 
            section.Value;
        return int.TryParse(configValue, out var value) ? value : 
            throw new InvalidOperationException($"Invalid integer value '{configValue}' for configuration key '{key}'.");
    }
    
    public T? GetConfigEnum<T>(string key, IConfiguration? subsection = null)
    {
        var section = subsection?.GetSection(key) ?? Cfg.GetSection(key);
        if (string.IsNullOrEmpty(section.Value))
        {
            return default;
        }
        var value = EnvDict is not null ? 
            Formatter.FormatString(section.Value.AsSpan(), EnvDict).ToString() : 
            section.Value;
        return GetEnum<T>(section?.Value);
    }

    public T? GetEnum<T>(string? value)
    {
        if (value is null)
        {
            return default;
        }
        var type = typeof(T);
        var nullable = Nullable.GetUnderlyingType(type);
        var names = Enum.GetNames(nullable ?? type);
        foreach (var name in names)
        {
            if (string.Equals(value, name, StringComparison.OrdinalIgnoreCase))
            {
                return (T)Enum.Parse(nullable ?? type, name);
            }
        }
        return default;
    }

    public IEnumerable<string>? GetConfigEnumerable(string key, IConfiguration? subsection = null)
    {
        var section = subsection is not null ? subsection?.GetSection(key) : Cfg.GetSection(key);
        if (section.Exists() is false)
        {
            return null;
        }
        var children = section?.GetChildren().ToArray();
        if (children is null || (children.Length == 0 && section?.Value == ""))
        {
            return null;
        }

        if (EnvDict is not null)
        {
            return children
                .Where(c => string.IsNullOrEmpty(c.Value) is false)
                .Select(c => Formatter.FormatString(c.Value.AsSpan(), EnvDict).ToString());
        }
        return children
            .Where(c => string.IsNullOrEmpty(c.Value) is false)
            .Select(c => c.Value!);
    }

    public T? GetConfigFlag<T>(string key, IConfiguration? subsection = null)
    {
        var array = GetConfigEnumerable(key, subsection)?.ToArray();
        if (array is null)
        {
            return default;
        }

        var type = typeof(T);
        var nullable = Nullable.GetUnderlyingType(type);
        var names = Enum.GetNames(nullable ?? type);

        T? result = default;
        foreach (var value in array)
        {
            foreach (var name in names)
            {
                if (string.Equals(value, name, StringComparison.OrdinalIgnoreCase))
                {
                    var e = (T)Enum.Parse(nullable ?? type, name);
                    if (result is null)
                    {
                        result = e;
                    }
                    else
                    {
                        result = (T)Enum.ToObject(type, Convert.ToInt32(result) | Convert.ToInt32(e));
                    }
                }
            }
        }
        return result;
    }

    public Dictionary<string, string>? GetConfigDict(IConfiguration config)
    {
        var result = new Dictionary<string, string>();
        foreach (var section in config.GetChildren())
        {
            if (section.Value is not null)
            {
                var value = EnvDict is not null ? 
                    Formatter.FormatString(section.Value.AsSpan(), EnvDict).ToString() : 
                    section.Value;
                
                result.TryAdd(section.Key, value);
            }
        }
        return result.Count == 0 ? null : result;
    }

    public string Serialize()
    {
        var defaults = ConfigDefaults.GetDefaults();
        var actual = SerializeConfig(Cfg);
        var merged = ConfigDefaults.MergeWithDefaults(defaults, actual);
        return merged?.ToJsonString(new JsonSerializerOptions() { WriteIndented = true }) ?? "{}";
    }

    public string SerializeWithComments()
    {
        var defaults = ConfigDefaults.GetDefaults();
        var actual = SerializeConfig(Cfg, stripPasswords: false);
        var merged = ConfigDefaults.MergeWithDefaults(defaults, actual);
        if (merged is not JsonObject root)
        {
            return "{}";
        }

        // If merged config matches defaults, output the exact JSONC template
        // This preserves all hand-crafted formatting, inline comments, and special sections
        var defaultsClone = ConfigDefaults.GetDefaults();
        if (defaultsClone is not null && JsonNodesEqual(root, defaultsClone))
        {
            return ConfigSchemaGenerator.DefaultConfigJsonc;
        }

        // Fallback: use template-based substitution for overridden configs
        return SubstituteTemplateValues(root);
    }

    /// <summary>
    /// Walks the JSONC template and substitutes values from the merged config where they differ from defaults.
    /// </summary>
    private string SubstituteTemplateValues(JsonObject merged)
    {
        var template = ConfigSchemaGenerator.DefaultConfigJsonc;
        var flatMerged = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        FlattenJsonNode(merged, "", flatMerged);

        var flatDefaults = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var defaults = ConfigDefaults.GetDefaults();
        if (defaults is not null)
        {
            FlattenJsonNode(defaults, "", flatDefaults);
        }

        // Find paths where values differ
        var changedPaths = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in flatMerged)
        {
            if (!flatDefaults.TryGetValue(kvp.Key, out var defaultVal) || defaultVal != kvp.Value)
            {
                changedPaths[kvp.Key] = kvp.Value;
            }
        }

        if (changedPaths.Count == 0)
        {
            return template;
        }

        // Walk the template and substitute changed values
        var lines = template.Split('\n');
        var sb = new StringBuilder();
        var pathStack = new List<string>();
        var inComment = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimEnd('\r').TrimStart();

            // Track multi-line comments
            if (trimmed.StartsWith("/*"))
            {
                inComment = true;
            }
            if (inComment)
            {
                sb.Append(lines[i].TrimEnd('\r'));
                if (i < lines.Length - 1) sb.Append('\n');
                if (trimmed.Contains("*/"))
                {
                    inComment = false;
                }
                continue;
            }

            // Skip single-line comments
            if (trimmed.StartsWith("//"))
            {
                sb.Append(lines[i].TrimEnd('\r'));
                if (i < lines.Length - 1) sb.Append('\n');
                continue;
            }

            // Track nesting
            if (trimmed.StartsWith('"'))
            {
                // Try to extract key from "key": ...
                var colonIdx = trimmed.IndexOf(':');
                if (colonIdx > 0)
                {
                    var key = trimmed[1..trimmed.IndexOf('"', 1)];
                    var currentPath = pathStack.Count > 0 ? string.Join(":", pathStack) + ":" + key : key;
                    var afterColon = trimmed[(colonIdx + 1)..].TrimStart();

                    // Check if this is a value line (not object or array open)
                    bool isObjectOpen = afterColon.StartsWith('{') && !afterColon.StartsWith("{  }") && !afterColon.TrimEnd(',').EndsWith('}');
                    bool isArrayOpen = afterColon.StartsWith('[') && !afterColon.TrimEnd(',').EndsWith(']');

                    if (isObjectOpen)
                    {
                        pathStack.Add(key);
                    }
                    else if (isArrayOpen)
                    {
                        pathStack.Add(key);
                    }
                    else if (changedPaths.TryGetValue(currentPath, out var newVal))
                    {
                        // Substitute the value
                        var indent = line.TrimEnd('\r')[..^(line.TrimEnd('\r').Length - line.TrimEnd('\r').Length + line.TrimEnd('\r').Length - line.TrimEnd('\r').TrimStart().Length)];
                        var endsWithComma = trimmed.TrimEnd().EndsWith(',');
                        var inlineComment = "";

                        // Preserve inline comments
                        var valueAndRest = afterColon;
                        var commentIdx = FindInlineCommentIndex(valueAndRest);
                        if (commentIdx >= 0)
                        {
                            inlineComment = " " + valueAndRest[commentIdx..].TrimEnd(',').TrimEnd();
                            if (endsWithComma && !inlineComment.EndsWith(','))
                            {
                                // comma was after inline comment
                            }
                        }

                        var prefix = line.TrimEnd('\r')[..(line.TrimEnd('\r').IndexOf(':') + 2)];
                        var comma = endsWithComma ? "," : "";
                        sb.Append($"{prefix}{newVal ?? "null"}{comma}{inlineComment}");
                        if (i < lines.Length - 1) sb.Append('\n');
                        continue;
                    }
                }
            }

            // Track closing braces
            if (trimmed.StartsWith('}') || trimmed.StartsWith(']'))
            {
                if (pathStack.Count > 0)
                {
                    pathStack.RemoveAt(pathStack.Count - 1);
                }
            }

            sb.Append(lines[i].TrimEnd('\r'));
            if (i < lines.Length - 1) sb.Append('\n');
        }

        return sb.ToString();
    }

    private static int FindInlineCommentIndex(string s)
    {
        bool inString = false;
        bool escape = false;
        for (int i = 0; i < s.Length - 1; i++)
        {
            if (escape) { escape = false; continue; }
            if (s[i] == '\\') { escape = true; continue; }
            if (s[i] == '"') { inString = !inString; continue; }
            if (!inString && s[i] == '/' && s[i + 1] == '/')
            {
                return i;
            }
        }
        return -1;
    }

    private static void FlattenJsonNode(JsonNode node, string prefix, Dictionary<string, string?> result)
    {
        if (node is JsonObject obj)
        {
            foreach (var kvp in obj)
            {
                var path = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}:{kvp.Key}";
                if (kvp.Value is JsonObject || kvp.Value is JsonArray)
                {
                    FlattenJsonNode(kvp.Value, path, result);
                }
                else
                {
                    result[path] = kvp.Value?.ToJsonString();
                }
            }
        }
        else if (node is JsonArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                var path = $"{prefix}:{i}";
                if (arr[i] is JsonObject || arr[i] is JsonArray)
                {
                    FlattenJsonNode(arr[i]!, path, result);
                }
                else
                {
                    result[path] = arr[i]?.ToJsonString();
                }
            }
        }
    }

    private static bool JsonNodesEqual(JsonNode? a, JsonNode? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;

        if (a is JsonObject aObj && b is JsonObject bObj)
        {
            if (aObj.Count != bObj.Count) return false;
            foreach (var kvp in aObj)
            {
                if (!bObj.ContainsKey(kvp.Key)) return false;
                if (!JsonNodesEqual(kvp.Value, bObj[kvp.Key])) return false;
            }
            return true;
        }

        if (a is JsonArray aArr && b is JsonArray bArr)
        {
            if (aArr.Count != bArr.Count) return false;
            for (int i = 0; i < aArr.Count; i++)
            {
                if (!JsonNodesEqual(aArr[i], bArr[i])) return false;
            }
            return true;
        }

        return a.ToJsonString() == b.ToJsonString();
    }

    public string FilterConfig(string filter)
    {
        var source = SerializeWithComments();
        var lines = source.Split('\n');

        // Parse entries inside the outermost { }
        var entries = ParseJsoncEntries(lines, 1, lines.Length - 1);

        // Collect filtered blocks (each block = list of lines, no trailing comma on last line)
        var blocks = CollectFilteredBlocks(entries, lines, filter);
        if (blocks.Count == 0)
        {
            return $"// No results for \"{filter}\"";
        }

        var sb = new StringBuilder();
        sb.AppendLine("{");
        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            for (int j = 0; j < block.Count; j++)
            {
                var line = block[j];
                if (j == block.Count - 1)
                {
                    line = StripTrailingComma(line);
                    if (i < blocks.Count - 1)
                    {
                        line = AddTrailingComma(line);
                    }
                }
                sb.AppendLine(line);
            }
        }
        sb.Append('}');
        return sb.ToString();
    }

    private sealed class JsoncEntry
    {
        public int CommentStart; // first line of preceding comments/blanks
        public int KeyLine;      // the "key": value or "key": { line
        public int EndLine;      // last line (value line for leaves, } line for sections)
        public string Key = "";
        public bool IsSection;
        public List<JsoncEntry> Children = [];
    }

    private static List<JsoncEntry> ParseJsoncEntries(string[] lines, int start, int end)
    {
        var entries = new List<JsoncEntry>();
        int i = start;
        while (i < end)
        {
            var trimmed = lines[i].AsSpan().Trim();

            // Skip pure blank lines and collect comment start position
            if (trimmed.IsEmpty || trimmed.StartsWith("//"))
            {
                // This might be the start of a new entry's comment block
                int commentStart = i;
                while (i < end)
                {
                    var t = lines[i].AsSpan().Trim();
                    if (t.IsEmpty || t.StartsWith("//"))
                    {
                        i++;
                        continue;
                    }
                    break;
                }
                if (i >= end) break;

                // Now lines[i] should be a key line
                var entry = ParseKeyLine(lines, commentStart, i, end);
                if (entry is not null)
                {
                    entries.Add(entry);
                    i = entry.EndLine + 1;
                }
                else
                {
                    i++;
                }
            }
            else if (trimmed.Length > 0 && trimmed[0] == '"')
            {
                // Key line without preceding comments
                var entry = ParseKeyLine(lines, i, i, end);
                if (entry is not null)
                {
                    entries.Add(entry);
                    i = entry.EndLine + 1;
                }
                else
                {
                    i++;
                }
            }
            else
            {
                i++;
            }
        }
        return entries;
    }

    private static JsoncEntry? ParseKeyLine(string[] lines, int commentStart, int keyLine, int end)
    {
        var line = lines[keyLine];
        var trimmed = line.AsSpan().Trim();

        // Extract key name from "key": ...
        if (trimmed.Length == 0 || trimmed[0] != '"') return null;
        int closeQuote = trimmed[1..].IndexOf('"');
        if (closeQuote < 0) return null;
        closeQuote += 1; // adjust for the slice offset
        var key = trimmed[1..closeQuote].ToString();

        // Check if value opens a section
        var afterKey = trimmed[(closeQuote + 1)..].TrimStart();
        if (afterKey.Length > 0 && afterKey[0] == ':')
        {
            var valueStart = afterKey[1..].TrimStart();
            if (valueStart.Length > 0 && valueStart[0] == '{')
            {
                // Section — find matching closing }
                int depth = 0;
                for (int i = keyLine; i < end; i++)
                {
                    var lt = lines[i].AsSpan().Trim();
                    if (lt.StartsWith("//")) continue;
                    depth += CountBraces(lines[i]);
                    if (depth == 0)
                    {
                        var entry = new JsoncEntry
                        {
                            CommentStart = commentStart,
                            KeyLine = keyLine,
                            EndLine = i,
                            Key = key,
                            IsSection = true,
                            Children = ParseJsoncEntries(lines, keyLine + 1, i)
                        };
                        return entry;
                    }
                }
                // If we can't find matching brace, treat as leaf
            }
        }

        // Leaf entry
        return new JsoncEntry
        {
            CommentStart = commentStart,
            KeyLine = keyLine,
            EndLine = keyLine,
            Key = key,
            IsSection = false
        };
    }

    private static int CountBraces(string line)
    {
        int count = 0;
        bool inString = false;
        var span = line.AsSpan();
        // Skip comment lines
        if (span.TrimStart().StartsWith("//")) return 0;
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] == '\\' && inString) { i++; continue; }
            if (span[i] == '"') { inString = !inString; continue; }
            if (!inString)
            {
                if (span[i] == '{') count++;
                else if (span[i] == '}') count--;
            }
        }
        return count;
    }

    private static bool EntryMatches(JsoncEntry entry, string[] lines, string filter)
    {
        var cmp = StringComparison.OrdinalIgnoreCase;
        // Check key name
        if (entry.Key.Contains(filter, cmp)) return true;
        // Check comment and value lines
        for (int i = entry.CommentStart; i <= Math.Min(entry.KeyLine, entry.EndLine); i++)
        {
            if (lines[i].Contains(filter, cmp)) return true;
        }
        // For leaf entries, check the value line
        if (!entry.IsSection && lines[entry.KeyLine].Contains(filter, cmp)) return true;
        // For sections, check if any child matches
        if (entry.IsSection)
        {
            foreach (var child in entry.Children)
            {
                if (EntryMatches(child, lines, filter)) return true;
            }
        }
        return false;
    }

    private static bool EntryDirectlyMatches(JsoncEntry entry, string[] lines, string filter)
    {
        var cmp = StringComparison.OrdinalIgnoreCase;
        if (entry.Key.Contains(filter, cmp)) return true;
        for (int i = entry.CommentStart; i <= entry.KeyLine; i++)
        {
            if (lines[i].Contains(filter, cmp)) return true;
        }
        return false;
    }

    private static List<List<string>> CollectFilteredBlocks(List<JsoncEntry> entries, string[] lines, string filter)
    {
        var blocks = new List<List<string>>();
        foreach (var entry in entries)
        {
            if (!EntryMatches(entry, lines, filter)) continue;

            if (!entry.IsSection)
            {
                // Leaf: copy comment lines + value line
                var block = ExtractLines(lines, entry.CommentStart, entry.EndLine);
                StripTrailingCommaFromLastLine(block);
                blocks.Add(block);
            }
            else if (EntryDirectlyMatches(entry, lines, filter))
            {
                // Section name/comment matches — include entire section
                var block = ExtractLines(lines, entry.CommentStart, entry.EndLine);
                StripTrailingCommaFromLastLine(block);
                blocks.Add(block);
            }
            else
            {
                // Section has matching children — include section wrapper with only matched children
                var childBlocks = CollectFilteredBlocks(entry.Children, lines, filter);
                if (childBlocks.Count == 0) continue;

                var block = new List<string>();
                // Section comments and opening line
                for (int i = entry.CommentStart; i <= entry.KeyLine; i++)
                {
                    block.Add(lines[i]);
                }
                // Matched children with commas between them
                for (int ci = 0; ci < childBlocks.Count; ci++)
                {
                    var cb = childBlocks[ci];
                    if (ci < childBlocks.Count - 1)
                    {
                        AddTrailingCommaToLastLine(cb);
                    }
                    block.AddRange(cb);
                }
                // Closing line
                block.Add(StripTrailingComma(lines[entry.EndLine]));
                blocks.Add(block);
            }
        }
        return blocks;
    }

    private static List<string> ExtractLines(string[] lines, int start, int end)
    {
        var result = new List<string>(end - start + 1);
        for (int i = start; i <= end; i++)
        {
            result.Add(lines[i]);
        }
        return result;
    }

    private static int FindJsonContentEnd(string line)
    {
        // Find the end of JSON content, skipping inline // comments
        bool inString = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\' && inString) { i++; continue; }
            if (line[i] == '"') { inString = !inString; continue; }
            if (!inString && i + 1 < line.Length && line[i] == '/' && line[i + 1] == '/')
            {
                // Found inline comment — JSON content ends before this
                int end = i;
                while (end > 0 && char.IsWhiteSpace(line[end - 1])) end--;
                return end;
            }
        }
        // No inline comment — trim trailing whitespace
        int e = line.Length;
        while (e > 0 && char.IsWhiteSpace(line[e - 1])) e--;
        return e;
    }

    private static string StripTrailingComma(string line)
    {
        int end = FindJsonContentEnd(line);
        if (end > 0 && line[end - 1] == ',')
        {
            return line[..(end - 1)] + line[end..];
        }
        return line;
    }

    private static string AddTrailingComma(string line)
    {
        int end = FindJsonContentEnd(line);
        if (end > 0 && line[end - 1] != ',')
        {
            return line[..end] + "," + line[end..];
        }
        return line;
    }

    private static void StripTrailingCommaFromLastLine(List<string> block)
    {
        for (int i = block.Count - 1; i >= 0; i--)
        {
            var trimmed = block[i].AsSpan().Trim();
            if (trimmed.IsEmpty || trimmed.StartsWith("//")) continue;
            block[i] = StripTrailingComma(block[i]);
            break;
        }
    }

    private static void AddTrailingCommaToLastLine(List<string> block)
    {
        for (int i = block.Count - 1; i >= 0; i--)
        {
            var trimmed = block[i].AsSpan().Trim();
            if (trimmed.IsEmpty || trimmed.StartsWith("//")) continue;
            block[i] = AddTrailingComma(block[i]);
            break;
        }
    }

    /// <summary>
    /// Validates configuration keys against known defaults.
    /// Returns the validation mode and list of unknown key paths.
    /// Mode is "Ignore", "Warning" (default), or "Error".
    /// </summary>
    public (string mode, List<string> warnings) ValidateConfigKeys()
    {
        var cfgCfg = Cfg.GetSection("Config");
        var mode = GetConfigStr("ValidateConfigKeys", cfgCfg) ?? "Warning";
        if (string.Equals(mode, "Ignore", StringComparison.OrdinalIgnoreCase))
        {
            return (mode, []);
        }
        var defaults = ConfigDefaults.GetDefaults();
        var actual = SerializeConfig(Cfg);
        return (mode, ConfigDefaults.FindUnknownConfigKeys(defaults, actual));
    }

    internal JsonNode? SerializeConfig(IConfiguration config, bool stripPasswords = true)
    {
        JsonObject obj = [];

        foreach (var child in config.GetChildren())
        {
            if (child.Path.EndsWith(":0"))
            {
                var arr = new JsonArray();
                foreach (var arrayChild in config.GetChildren())
                {
                    arr.Add(SerializeConfig(arrayChild, stripPasswords));
                }
                return arr;
            }
            obj.Add(child.Key, SerializeConfig(child, stripPasswords));
        }

        if (obj.Count == 0 && config is IConfigurationSection section)
        {
            if (section.Value is null)
            {
                return null;
            }
            var value = EnvDict is not null ?
                Formatter.FormatString(section.Value.AsSpan(), EnvDict).ToString() :
                section.Value;
            if (bool.TryParse(value, out bool boolean))
            {
                return JsonValue.Create(boolean);
            }
            // Don't parse strings with leading zeros as numbers (e.g., PostgreSQL error codes "08000")
            bool hasLeadingZero = value.Length > 1 && value[0] == '0' && char.IsDigit(value[1]);
            if (!hasLeadingZero)
            {
                if (decimal.TryParse(value, out decimal real))
                {
                    return JsonValue.Create(real);
                }
                else if (long.TryParse(value, out long integer))
                {
                    return JsonValue.Create(integer);
                }
            }
            if (stripPasswords && section.Path.StartsWith("ConnectionStrings:"))
            {
                return JsonValue.Create(string.Join(';',
                    value.Split(';').Where(p => p.StartsWith("password", StringComparison.OrdinalIgnoreCase) is false)));
            }
            return JsonValue.Create(value);
        }

        return obj;
    }

    private static void LoadEnvFile(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = trimmed[..separatorIndex].Trim();
            var value = trimmed[(separatorIndex + 1)..].Trim();

            // Remove surrounding quotes if present
            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            Environment.SetEnvironmentVariable(key, value);
        }
    }

    private (List<(string fileName, bool optional)> configFiles, string[] commanLineArgs) BuildFromArgs(string[] args)
    {
        var configFiles = new List<(string fileName, bool optional)>();
        var commandLineArgs = new List<string>();

        bool nextIsOptional = false;
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith('-'))
            {
                var lower = arg.ToLowerInvariant();
                if (lower is "-o" or "--optional")
                {
                    nextIsOptional = true;
                }
                else if (lower.StartsWith("--config="))
                {
                    commandLineArgs.Add("--config");
                    ConfigFilter = arg[9..];
                }
                else if (string.Equals("--config", lower))
                {
                    commandLineArgs.Add(arg);
                    // Peek: if next arg exists and doesn't start with '-', it's the filter
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                    {
                        ConfigFilter = args[++i];
                    }
                }
                else if (arg.StartsWith("--") && arg.Contains('='))
                {
                    commandLineArgs.Add(arg);
                }
                else if (lower is "--json" or "--validate" or "--endpoints")
                {
                    // Known flags handled by Program.cs, skip silently
                }
                else
                {
                    throw new ArgumentException($"Unknown parameter {arg}");
                }
            }
            else
            {
                configFiles.Add((arg, nextIsOptional));
                nextIsOptional = false;
            }
        }
        return (configFiles, commandLineArgs.ToArray());
    }

    public IConfiguration TransformSection(IConfigurationSection section)
    {
        if (EnvDict is null)
        {
            return section;
        }

        var dict = new Dictionary<string, string?>();
        CollectTransformedValues(section, "", dict);

        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }

    private void CollectTransformedValues(IConfigurationSection section, string prefix, Dictionary<string, string?> dict)
    {
        foreach (var child in section.GetChildren())
        {
            var key = string.IsNullOrEmpty(prefix) ? child.Key : $"{prefix}:{child.Key}";

            if (child.Value is not null)
            {
                dict[key] = Formatter.FormatString(child.Value.AsSpan(), EnvDict!).ToString();
            }

            CollectTransformedValues(child, key, dict);
        }
    }
}
