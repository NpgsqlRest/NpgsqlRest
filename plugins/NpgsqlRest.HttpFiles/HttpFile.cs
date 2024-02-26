﻿using System.Text;
using Npgsql;

namespace NpgsqlRest.HttpFiles;

public class HttpFile(HttpFileOptions httpFileOptions) : IEndpointCreateHandler
{
    public HttpFile() : this (new HttpFileOptions()) { }

    private readonly HashSet<string> _initializedFiles = [];
    private readonly Dictionary<string, StringBuilder> _fileContent = [];

    private bool _endpoint = false;
    private bool _file = false;

    private IApplicationBuilder _builder = default!;
    private ILogger? _logger;
    
    public void Setup(IApplicationBuilder builder, ILogger? logger)
    {
        _builder = builder;
        _logger = logger;
    }

    public void Handle(Routine routine, RoutineEndpoint endpoint)
    {
        if (httpFileOptions.Option == HttpFileOption.Disabled)
        {
            return;
        }

        _endpoint = httpFileOptions.Option is HttpFileOption.Endpoint or HttpFileOption.Both;
        _file = httpFileOptions.Option is HttpFileOption.File or HttpFileOption.Both;

        var fileName = FormatFileName();
        if (_initializedFiles.Add(fileName))
        {
            StringBuilder content = new();
            content.AppendLine(string.Concat("@host=", GetHost()));
            content.AppendLine();
            _fileContent[fileName] = content;
        }

        StringBuilder sb = new();

        if (httpFileOptions.CommentHeader != CommentHeader.None)
        {
            switch (httpFileOptions.CommentHeader)
            {
                case CommentHeader.Simple:
                    {
                        foreach (var line in routine.SimpleDefinition.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (line == "\r")
                            {
                                continue;
                            }
                            sb.AppendLine(string.Concat("// ", line.TrimEnd('\r')));
                        }

                        WriteComment(sb, routine);
                        break;
                    }
                case CommentHeader.Full:
                    {
                        foreach (var line in routine.FullDefinition.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (line == "\r")
                            {
                                continue;
                            }
                            sb.AppendLine(string.Concat("// ", line.TrimEnd('\r')));
                        }

                        WriteComment(sb, routine);
                        break;
                    }
            }
        }
        if (endpoint.ParamNames.Length == 0 || endpoint.RequestParamType != RequestParamType.QueryString)
        {
            sb.AppendLine(string.Concat(endpoint.Method, " {{host}}", endpoint.Url));
        }

        if (endpoint.ParamNames.Length > 0 && endpoint.RequestParamType == RequestParamType.QueryString)
        {
            var line = string.Concat(endpoint.Method, " {{host}}", endpoint.Url, "?",
                string.Join("&", endpoint
                    .ParamNames
                    .Where((p, i) =>
                    {
                        if (endpoint.BodyParameterName is not null)
                        {
                            if (string.Equals(p, endpoint.BodyParameterName, StringComparison.Ordinal) ||
                                string.Equals(routine.ParamNames[i], endpoint.BodyParameterName, StringComparison.Ordinal))
                            {
                                return false;
                            }
                        }
                        
                        return true;
                    })
                    .Select((p, i) =>
                    {
                        var descriptor = routine.ParamTypeDescriptor[i];
                        var value = SampleValueUnquoted(i, descriptor);
                        if (descriptor.IsArray)
                        {
                            return string.Join("&", value.Split(',').Select(v => $"{p}={Uri.EscapeDataString(v)}"));
                        }
                        return $"{p}={Uri.EscapeDataString(value)}";
                    })));

            sb.AppendLine(line.EndsWith('?') ? line[..^1] : line);
            
            if (endpoint.BodyParameterName is not null)
            {
                for(int i = 0; i < routine.ParamCount; i++)
                {
                    if (string.Equals(endpoint.ParamNames[i], endpoint.BodyParameterName, StringComparison.Ordinal) ||
                        string.Equals(routine.ParamNames[i], endpoint.BodyParameterName, StringComparison.Ordinal))
                    {
                        sb.AppendLine();
                        sb.AppendLine(SampleValueUnquoted(i, routine.ParamTypeDescriptor[i]));
                        break;
                    }
                }
            }
        }

        if (endpoint.ParamNames.Length > 0 && endpoint.RequestParamType == RequestParamType.BodyJson)
        {
            sb.AppendLine("content-type: application/json");
            sb.AppendLine();
            sb.AppendLine("{");
            foreach(var (p, i)  in endpoint.ParamNames.Select((p, i) => (p, i)))
            {
                sb.AppendLine(string.Concat(
                    "    \"", p, 
                    "\": ",
                    SampleValue(i, routine.ParamTypeDescriptor[i]),
                    i == endpoint.ParamNames.Length - 1 ? "" : ","));
            }
            sb.AppendLine("}");
        }

        sb.AppendLine();
        sb.AppendLine("###");
        sb.AppendLine();

        _fileContent[fileName].Append(sb);
        return;

        string FormatFileName()
        {
            var name = GetName();
            var schema = httpFileOptions.FileMode != HttpFileMode.Schema ? "" : string.Concat("_", routine.Schema);
            return string.Concat(string.Format(httpFileOptions.NamePattern, name, schema), ".http");
        }
    }

    public void Cleanup()
    {
        if (httpFileOptions.Option == HttpFileOption.Disabled)
        {
            return;
        }

        if (_endpoint)
        {
            foreach(var (fileName, content) in _fileContent)
            {
                var path = $"/{fileName}";
                _builder.Use(async (context, next) =>
                {
                    if (!(string.Equals(context.Request.Method, "GET", StringComparison.OrdinalIgnoreCase) && 
                        string.Equals(context.Request.Path, path, StringComparison.Ordinal)))
                    {
                        await next(context);
                        return;
                    }

                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "text/plain";
                    await context.Response.WriteAsync(content.ToString());
                });

                _logger?.LogInformation("Exposed HTTP file content on URL: {0}{1}", GetHost(), path);
            }
        }

        if (_file)
        {
            foreach(var (fileName, content) in _fileContent)
            {
                var fullFileName = Path.Combine(Environment.CurrentDirectory, fileName);
                if (!httpFileOptions.FileOverwrite && File.Exists(fullFileName))
                {
                    continue;
                }
                File.WriteAllText(fullFileName, content.ToString());
                _logger?.LogInformation("Created HTTP file: {0}", fullFileName);
            }
        }
    }

    private void WriteComment(StringBuilder sb, Routine routine)
    {
        if (httpFileOptions.CommentHeaderIncludeComments is false || string.IsNullOrEmpty(routine.Comment?.Trim()))
        {
            return;
        }
        var lines = routine
            .Comment
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .ToArray();
        if (lines.Length > 0)
        {
            sb.AppendLine("//");
        }
        else
        {
            return;
        }
        foreach (var (line, index) in lines.Select((l, i) => (l, i)))
        {
            if (line == "\r" && index > 0)
            {
                continue;
            }
            var comment = line.Replace("'", "''").TrimEnd('\r');
            if (index == 0)
            {
                comment = string.Concat($"comment on function {routine.Schema}.{routine.Name} is '", comment);
            }
            else if (index == lines.Length - 1)
            {
                comment = string.Concat(comment, "';");
            }
            sb.AppendLine(string.Concat("// ", comment));
        }
    }

    private static string SampleValueUnquoted(int i, TypeDescriptor type)
    {
        return SampleValue(i, type, isQuery: true).Trim('"');
    }

    private static string SampleValue(int i, TypeDescriptor type, bool isQuery = false)
    {
        var counter = i;

        if (type.IsNumeric)
        {
            if (string.Equals(type.Type, "real", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type.Type, "double precision", StringComparison.OrdinalIgnoreCase))
            {
                if (type.IsArray)
                {
                    return GetArray(
                        $"{counter + 2}.{counter + 3}",
                        $"{counter + 3}.{counter + 4}",
                        $"{counter + 4}.{counter + 5}",
                        false);
                }
                return $"{counter+1}.{counter+2}";
            }
            else

            if (type.IsArray)
            {
                return GetArray((counter + 1).ToString(), (counter + 2).ToString(), (counter + 3).ToString(), false);
            }
            return (counter + 1).ToString();
        }

        if (type.IsBoolean)
        {
            if (type.IsArray)
            {
                return GetArray(
                    ((counter + 1) % 2 == 1).ToString().ToLower(), 
                    ((counter + 2) % 2 == 1).ToString().ToLower(), 
                    ((counter + 3) % 2 == 1).ToString().ToLower(),
                    false);
            }
            return (counter % 2 == 1).ToString().ToLower();
        }

        if (type.Type == "uuid")
        {
            if (type.IsArray)
            {
                return GetArray(
                    new Guid().ToString(),
                    new Guid().ToString(),
                    new Guid().ToString(),
                    true);
            }
            return Quote(new Guid().ToString());
        }

        if (type.IsDateTime)
        {
            var now = new DateTime(2024, counter % 12 + 1, counter % 28 + 1, counter % 24, counter % 60, counter % 60);
            if (type.IsArray)
            {
                return GetArray(
                    now.AddDays(-2).ToString("O")[..22],
                    now.AddDays(-1).ToString("O")[..22],
                    now.ToString("O")[..22],
                    true);
            }
            return Quote(now.ToString("O")[..22]);
        }

        if (type.IsDate)
        {
            var now = new DateTime(2024, counter % 12 + 1, counter % 28 + 1, counter % 24, counter % 60, counter % 60);
            if (type.IsArray)
            {
                return GetArray(
                    now.AddDays(-2).ToString("yyyy-MM-dd"),
                    now.AddDays(-1).ToString("yyyy-MM-dd"),
                    now.ToString("yyyy-MM-dd"),
                    true);
            }
            return Quote(now.ToString("yyyy-MM-dd"));
        }

        if (type.IsJson)
        {
            if (type.IsArray)
            {
                return GetArray("{}", "{}", "{}", false);
            }
            return "{}";
        }

        if (string.Equals(type.Type, "jsonpath", StringComparison.OrdinalIgnoreCase))
        {
            if (type.IsArray)
            {
                return GetArray(
                    $"$.user.addresses[0].city",
                    $"$.user.addresses[1].street",
                    $"$.user.addresses[2].postalNumber",
                true);
            }
            return Quote($"$.user.addresses[0].city");
        }

        if (string.Equals(type.Type, "time without time zone", StringComparison.OrdinalIgnoreCase))
        {
            var now = new DateTime(2024, counter % 12 + 1, counter % 28 + 1, counter % 24, counter % 60, counter % 60);
            if (type.IsArray)
            {
                return GetArray(
                    now.AddDays(-2).TimeOfDay.ToString()[..^1],
                    now.AddDays(-1).TimeOfDay.ToString()[..^1],
                    now.TimeOfDay.ToString()[..^1],
                true);
            }
            return Quote(now.TimeOfDay.ToString()[..^1]);
        }

        if (string.Equals(type.Type, "time with time zone", StringComparison.OrdinalIgnoreCase))
        {
            var now = new DateTime(2024, counter % 12 + 1, counter % 28 + 1, counter % 24, counter % 60, counter % 60);
            if (type.IsArray)
            {
                return GetArray(
                    string.Concat(now.AddDays(-2).TimeOfDay.ToString()[..^1], "+01:00"),
                    string.Concat(now.AddDays(-1).TimeOfDay.ToString()[..^1], "+01:00"),
                    string.Concat(now.TimeOfDay.ToString()[..^1], "+01:00"),
                true);
            }
            return Quote(string.Concat(now.TimeOfDay.ToString()[..^1], "+01:00"));
        }

        if (string.Equals(type.Type, "interval", StringComparison.OrdinalIgnoreCase))
        {
            if (type.IsArray)
            {
                return GetArray(
                    $"{counter + 1} minutes {(counter + 2) % 60} seconds",
                    $"{counter + 2} minutes {(counter + 3) % 60} seconds",
                    $"{counter + 3} minutes {(counter + 4) % 60} seconds",
                true);
            }
            return Quote($"{counter} minutes {(counter + 1) % 60} seconds");
        }

        if (string.Equals(type.Type, "varbit", StringComparison.OrdinalIgnoreCase) || 
            string.Equals(type.Type, "bit varying", StringComparison.OrdinalIgnoreCase))
        {
            if (type.IsArray)
            {
                return GetArray(
                    $"110",
                    $"101",
                    $"011",
                true);
            }
            return Quote($"{(counter) % 2}{(counter+1) % 2}{(counter+2) % 2}");
        }

        if (string.Equals(type.Type, "inet", StringComparison.OrdinalIgnoreCase))
        {
            if (type.IsArray)
            {
                return GetArray(
                    $"192.168.5.{counter}",
                    $"192.168.5.{counter+1}",
                    $"192.168.5.{counter+2}",
                true);
            }
            return Quote($"192.168.5.{counter}");
        }

        if (string.Equals(type.Type, "macaddr", StringComparison.OrdinalIgnoreCase))
        {
            if (type.IsArray)
            {
                return GetArray(
                    "00-B0-D0-63-C2-26",
                    "00-B0-D0-63-C2-26",
                    "00-B0-D0-63-C2-26",
                true);
            }
            return Quote($"00-B0-D0-63-C2-26");
        }

        if (string.Equals(type.Type, "bytea", StringComparison.OrdinalIgnoreCase))
        {
            if (type.IsArray)
            {
                return GetArray(
                    "\\\\xFEADBAEF",
                    "\\\\xAEADBDEF",
                    "\\\\xEEADEEEF",
                true);
            }
            return Quote($"\\\\xDEADBEEF");
        }

        if (type.IsArray)
        {
            return GetArray(GetSubstring(counter), GetSubstring(counter + 1), GetSubstring(counter + 2), true);
        }

        return Quote(GetSubstring());

        static string Quote(string value) => string.Concat("\"", value, "\"");

        string GetArray(string v1, string v2, string v3, bool quoted)
        {
            if (isQuery)
            {
                return string.Concat(v1, ",", v2, ",", v3);
            }
            return quoted ? string.Concat("[", Quote(v1), ", ", Quote(v2), ", ", Quote(v3), "]") : string.Concat("[", v1, ", ", v2, ", ", v3, "]");
        }

        string GetSubstring(int? value = null) => ((value ?? counter) % 3) switch
        {
            0 => "ABC",
            1 => "XYZ",
            2 => "IJK",
            _ => "WTF"
        };
    }

    private string GetHost()
    {
        string? host = null;
        if (_builder is WebApplication app)
        {
            if (app.Urls.Count != 0)
            {
                host = app.Urls.FirstOrDefault();
            }
            else
            {
                var section = app.Configuration?.GetSection("ASPNETCORE_URLS");
                if (section?.Value is not null)
                {
                    host = section.Value.Split(";")?.LastOrDefault();
                }
            }
        }
        // default, assumed host
        host ??= "http://localhost:5000";
        return host;
    }

    private string GetName()
    {
        if (httpFileOptions.Name is not null)
        {
            return httpFileOptions.Name;
        }
        if (httpFileOptions.ConnectionString is not null)
        {
            return new NpgsqlConnectionStringBuilder(httpFileOptions.ConnectionString).Database ??
                   httpFileOptions.ConnectionString?.Split(";").Where(s => s.StartsWith("Database=")).FirstOrDefault()
                       ?.Split("=")?.Last() ?? "npgsqlrest";
        }
        return "npgsqlrest";
    }
}