using System.Text;
using Npgsql;
using NpgsqlRest;
using NpgsqlRest.CrudSource;
using NpgsqlRest.HttpFiles;
using NpgsqlRest.TsClient;

namespace NpgsqlRestClient;

public class Out
{
    public void NL() => Console.WriteLine();

    public void Logo()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
    _   __                   __  ____            __
   / | / /___  ____ ________/ / / __ \___  _____/ /_
  /  |/ / __ \/ __ `/ ___/ __ \/ /_/ / _ \/ ___/ __/
 / /|  / /_/ / /_/ (__  ) /_/ / _, _/  __(__  ) /_
/_/ |_/ .___/\__, /____/\__, /_/ |_|\___/____/\__/
     /_/    /____/        /_/
");
        Console.ResetColor();
    }

    public void Line(string line, ConsoleColor? color = null)
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

    public void Write(string line, ConsoleColor? color = null)
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

    public void JsonHighlight(string json)
    {
        if (Console.IsOutputRedirected)
        {
            Console.WriteLine(json);
            return;
        }

        foreach (var line in json.Split('\n'))
        {
            var trimmed = line.AsSpan().TrimStart();
            // leading whitespace
            if (trimmed.Length < line.Length)
            {
                Console.Write(line.AsSpan(0, line.Length - trimmed.Length));
            }

            if (trimmed.IsEmpty)
            {
                Console.WriteLine();
                continue;
            }

            // "key": value
            if (trimmed.Length > 0 && trimmed[0] == '"')
            {
                int closeQuote = IndexOfUnescapedQuote(trimmed, 1);
                if (closeQuote > 0)
                {
                    var afterQuote = trimmed[(closeQuote + 1)..].TrimStart();
                    if (afterQuote.Length > 0 && afterQuote[0] == ':')
                    {
                        // It's a key
                        Write("\"", ConsoleColor.DarkGray);
                        Write(trimmed[1..closeQuote].ToString(), ConsoleColor.Cyan);
                        Write("\"", ConsoleColor.DarkGray);
                        Write(": ", ConsoleColor.DarkGray);
                        var value = afterQuote[1..].TrimStart();
                        WriteJsonValue(value);
                        Console.WriteLine();
                        continue;
                    }
                    else
                    {
                        // It's a string value on its own line (array element)
                        WriteJsonValue(trimmed);
                        Console.WriteLine();
                        continue;
                    }
                }
            }

            // standalone structural or value tokens
            WriteJsonValue(trimmed);
            Console.WriteLine();
        }
        Console.ResetColor();
    }

    private void WriteJsonValue(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty) return;

        var trimmed = value.TrimEnd();
        bool trailingComma = trimmed.Length > 0 && trimmed[^1] == ',';
        var core = trailingComma ? trimmed[..^1] : trimmed;

        if (core.Length == 0)
        {
            if (trailingComma) Write(",", ConsoleColor.DarkGray);
            return;
        }

        char first = core[0];
        if (first == '"')
        {
            Write("\"", ConsoleColor.DarkGray);
            Write(core[1..^1].ToString(), ConsoleColor.Yellow);
            Write("\"", ConsoleColor.DarkGray);
        }
        else if (first is '{' or '}' or '[' or ']')
        {
            Write(core.ToString(), ConsoleColor.DarkGray);
        }
        else if (core is "null")
        {
            Write("null", ConsoleColor.DarkGray);
        }
        else if (core is "true" or "false" ||
                 (core.Length > 0 && (char.IsDigit(first) || first == '-')))
        {
            Write(core.ToString(), ConsoleColor.Green);
        }
        else
        {
            Console.Write(core);
        }

        if (trailingComma) Write(",", ConsoleColor.DarkGray);
    }

    private static int IndexOfUnescapedQuote(ReadOnlySpan<char> span, int start)
    {
        for (int i = start; i < span.Length; i++)
        {
            if (span[i] == '\\')
            {
                i++; // skip escaped char
                continue;
            }
            if (span[i] == '"') return i;
        }
        return -1;
    }

    public void Line((string str1, string str2)[] lines)
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
