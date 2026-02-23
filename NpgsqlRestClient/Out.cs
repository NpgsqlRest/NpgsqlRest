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

            // JSONC comments
            if (trimmed.StartsWith("//"))
            {
                Write(trimmed.ToString(), ConsoleColor.DarkGreen);
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

    public void JsonHighlightWithMatch(string json, string match)
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

            // JSONC comments
            if (trimmed.StartsWith("//"))
            {
                WriteWithMatchHighlight(trimmed.ToString(), ConsoleColor.DarkGreen, match);
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
                        Write("\"", ConsoleColor.DarkGray);
                        WriteWithMatchHighlight(trimmed[1..closeQuote].ToString(), ConsoleColor.Cyan, match);
                        Write("\"", ConsoleColor.DarkGray);
                        Write(": ", ConsoleColor.DarkGray);
                        var value = afterQuote[1..].TrimStart();
                        WriteJsonValueWithMatch(value, match);
                        Console.WriteLine();
                        continue;
                    }
                    else
                    {
                        WriteJsonValueWithMatch(trimmed, match);
                        Console.WriteLine();
                        continue;
                    }
                }
            }

            WriteJsonValueWithMatch(trimmed, match);
            Console.WriteLine();
        }
        Console.ResetColor();
    }

    private void WriteWithMatchHighlight(string text, ConsoleColor baseColor, string match)
    {
        int pos = 0;
        while (pos < text.Length)
        {
            int idx = text.IndexOf(match, pos, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                Write(text[pos..], baseColor);
                break;
            }
            if (idx > pos)
            {
                Write(text[pos..idx], baseColor);
            }
            // Invert colors for the match
            var prevBg = Console.BackgroundColor;
            var prevFg = Console.ForegroundColor;
            Console.BackgroundColor = baseColor;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.Write(text[idx..(idx + match.Length)]);
            Console.BackgroundColor = prevBg;
            Console.ForegroundColor = prevFg;
            pos = idx + match.Length;
        }
    }

    private void WriteJsonValueWithMatch(ReadOnlySpan<char> value, string match)
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
            WriteWithMatchHighlight(core[1..^1].ToString(), ConsoleColor.Yellow, match);
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
            WriteWithMatchHighlight(core.ToString(), ConsoleColor.Green, match);
        }
        else
        {
            WriteWithMatchHighlight(core.ToString(), Console.ForegroundColor, match);
        }

        if (trailingComma) Write(",", ConsoleColor.DarkGray);
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
        if (Console.IsOutputRedirected)
        {
            foreach (var (str1, str2) in lines)
            {
                Console.WriteLine($"{str1}\t{str2}");
            }
            return;
        }
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
