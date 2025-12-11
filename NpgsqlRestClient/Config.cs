using System.Collections;
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
        
        if (cfgCfg != null && GetConfigBool("ParseEnvironmentVariables", cfgCfg, true) is true)
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
        var json = SerializeConfig(Cfg);
        return json?.ToJsonString(new JsonSerializerOptions() { WriteIndented = true }) ?? "{}";
    }

    private JsonNode? SerializeConfig(IConfiguration config)
    {
        JsonObject obj = [];

        foreach (var child in config.GetChildren())
        {
            if (child.Path.EndsWith(":0"))
            {
                var arr = new JsonArray();
                foreach (var arrayChild in config.GetChildren())
                {
                    arr.Add(SerializeConfig(arrayChild));
                }
                return arr;
            }
            obj.Add(child.Key, SerializeConfig(child));
        }

        if (obj.Count == 0 && config is IConfigurationSection section)
        {
            var value = EnvDict is not null ? 
                Formatter.FormatString(section.Value.AsSpan(), EnvDict).ToString() : 
                section.Value;
            if (bool.TryParse(value, out bool boolean))
            {
                return JsonValue.Create(boolean);
            }
            else if (decimal.TryParse(value, out decimal real))
            {
                return JsonValue.Create(real);
            }
            else if (long.TryParse(value, out long integer))
            {
                return JsonValue.Create(integer);
            }
            if (section.Path.StartsWith("ConnectionStrings:"))
            {
                return JsonValue.Create(string.Join(';', 
                    value?.Split(';')?.Where(p => p.StartsWith("password", StringComparison.OrdinalIgnoreCase) is false) ?? []));
            }
            return JsonValue.Create(value);
        }

        return obj;
    }

    private (List<(string fileName, bool optional)> configFiles, string[] commanLineArgs) BuildFromArgs(string[] args)
    {
        var configFiles = new List<(string fileName, bool optional)>();
        var commandLineArgs = new List<string>();

        bool nextIsOptional = false;
        foreach (var arg in args)
        {
           
            if (arg.StartsWith('-'))
            {
                var lower = arg.ToLowerInvariant();
                if (lower is "-o" or "--optional")
                {
                    nextIsOptional = true;
                }
                else if ((arg.StartsWith("--") && arg.Contains('=')) || string.Equals("--config", lower))
                {
                    commandLineArgs.Add(arg);
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
}
