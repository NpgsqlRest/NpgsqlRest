using System.Text;
using Npgsql;
using NpgsqlRest;
using NpgsqlRest.CrudSource;
using NpgsqlRest.HttpFiles;
using NpgsqlRest.TsClient;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace NpgsqlRestClient;

public class Arguments
{
    public bool Parse(string[] args)
    {
        if (args.Length >= 2 && string.Equals(args[0], "hash", StringComparison.CurrentCultureIgnoreCase))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            var hasher = new NpgsqlRest.Auth.PasswordHasher();
            Console.WriteLine(hasher.HashPassword(args[1]));
            Console.ResetColor();
            return false;
        }
        
        if (args.Length >= 3 && string.Equals(args[0], "basic_auth", StringComparison.CurrentCultureIgnoreCase))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(string.Concat("Authorization: Basic ", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{args[1]}:{args[2]}"))));
            Console.ResetColor();
            return false;
        }

        if (args.Any(a => a.ToLowerInvariant() is "-v" or "--version" or "-h" or "--help") is false)
        {
            return true;
        }

        if (args.Any(a => a.ToLowerInvariant() is "-h" or "--help") is true)
        {
            Line("Usage:");
            Line([
                ("npgsqlrest", "Run with the optional default configuration files: appsettings.json and appsettings.Development.json. If these file are not found, default configuration setting is used (see https://github.com/NpgsqlRest/NpgsqlRest/blob/master/NpgsqlRestClient/appsettings.json)."),
                ("npgsqlrest [files...]", "Run with the custom configuration files. All configuration files are required. Any configuration values will override default values in order of appearance."),
                ("npgsqlrest [file1 -o file2...]", "Use the -o switch to mark the next configuration file as optional. The first file after the -o switch is optional."),
                ("npgsqlrest [file1 --optional file2...]", "Use --optional switch to mark the next configuration file as optional. The first file after the --optional switch is optional."),
                ("Note:", "Values in the later file will override the values in the previous one."),
                (" ", " "),
                ("npgsqlrest [--key=value]", "Override the configuration with this key with a new value (case insensitive, use : to separate sections). "),
                (" ", " "),
                ("npgsqlrest -v, --version", "Show version information."),
                ("npgsqlrest -h, --help", "Show command line help."),
                ("npgsqlrest config", "Dump current configuration to console and exit."),
                ("npgsqlrest hash [value]", "Hash value with default hasher and print to console."),
                ("npgsqlrest basic_auth [username] [password]", "Print out basic basic auth header value in format 'Authorization: Basic base64(username:password)'."),
                ("npgsqlrest encrypt [value]", "Encrypt string using default data protection and print to console."),
                ("npgsqlrest encrypted_basic_auth [username] [password]", "Print out basic basic auth header value in format 'Authorization: Basic base64(username:password)' where password is encrypted with default data protection."),
                (" ", " "),
                (" ", " "),
                ("Examples:", " "),
                ("Example: use two config files", "npgsqlrest appsettings.json appsettings.Development.json"),
                ("Example: second config file optional", "npgsqlrest appsettings.json -o appsettings.Development.json"),
                ("Example: override ApplicationName config", "npgsqlrest --applicationname=Test"),
                ("Example: override Auth:CookieName config", "npgsqlrest --auth:cookiename=Test"),
                (" ", " "),
                ]);
        }

        if (args.Any(a => a.ToLowerInvariant() is "-v" or "--version") is true)
        {
            var versions = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.Split(' ');
            Line("Versions:");
            Line([
                (versions[0], versions[1]),
                ("Client Build", System.Reflection.Assembly.GetAssembly(typeof(Program))?.GetName()?.Version?.ToString() ?? "-"),
                ("System.Text.Json", System.Reflection.Assembly.GetAssembly(typeof(System.Text.Json.JsonCommentHandling))?.GetName()?.Version?.ToString() ?? "-"),
                ("ExcelDataReader", System.Reflection.Assembly.GetAssembly(typeof(ExcelDataReader.IExcelDataReader))?.GetName()?.Version?.ToString() ?? "-"),
                ("Serilog.AspNetCore", System.Reflection.Assembly.GetAssembly(typeof(Serilog.AspNetCore.RequestLoggingOptions))?.GetName()?.Version?.ToString() ?? "-"),
                ("Npgsql", System.Reflection.Assembly.GetAssembly(typeof(NpgsqlConnection))?.GetName()?.Version?.ToString() ?? "-"),
                ("NpgsqlRest", System.Reflection.Assembly.GetAssembly(typeof(NpgsqlRestOptions))?.GetName()?.Version?.ToString() ?? "-"),
                ("NpgsqlRest.HttpFiles", System.Reflection.Assembly.GetAssembly(typeof(HttpFileOptions))?.GetName()?.Version?.ToString() ?? "-"),
                ("NpgsqlRest.TsClient", System.Reflection.Assembly.GetAssembly(typeof(TsClientOptions))?.GetName()?.Version?.ToString() ?? "-"),
                ("NpgsqlRest.CrudSource", System.Reflection.Assembly.GetAssembly(typeof(CrudSource))?.GetName()?.Version?.ToString() ?? "-"),
                (" ", " "),
                ("CurrentDirectory", Directory.GetCurrentDirectory())
                ]);
            NL();
        }
        return false;
    }

    public (List<(string fileName, bool optional)> configFiles, string[] commanLineArgs) BuildFromArgs(string[] args)
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
                else if (arg.StartsWith("--") && arg.Contains('='))
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

    private void NL() => Console.WriteLine();

    private void Line(string line, ConsoleColor? color = null)
    {
        if (color is not null)
        {
            Console.ForegroundColor = color.Value;
        }
        Console.WriteLine(line);
        if (color is not null)
        {
            Console.ResetColor();
        }
    }

    private void Write(string line, ConsoleColor? color = null)
    {
        if (color is not null)
        {
            Console.ForegroundColor = color.Value;
        }
        Console.Write(line);
        if (color is not null)
        {
            Console.ResetColor();
        }
    }

    private void Line((string str1, string str2)[] lines)
    {
        var pos = lines.Select(l => l.str1.Length).Max() + 1;
        int consoleWidth = Console.WindowWidth;
        foreach (var (str1, str2) in lines)
        {
            Write(str1, ConsoleColor.Yellow);
            Console.CursorLeft = pos;
            var words = str2.Split(' ');
            var line = new StringBuilder(words[0]);
            for (int i = 1; i < words.Length; i++)
            {
                if (line.Length + words[i].Length >= consoleWidth - pos)
                {
                    Line(line.ToString());
                    line.Clear();
                    Console.CursorLeft = pos - 1;
                }
                line.Append(' ' + words[i]);
            }
            if (line.Length > 0)
            {
                Line(line.ToString());
            }
        }
    }
}
