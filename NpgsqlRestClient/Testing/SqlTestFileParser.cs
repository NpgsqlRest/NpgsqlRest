using System.Text;

namespace NpgsqlRestClient.Testing;

/// <summary>
/// Splits a .test.sql file into an ordered list of steps (SQL statements, embedded HTTP requests, and
/// include lines), preserving interleaving. A char-by-char state machine handles ';' statement splitting,
/// line/block comments, single quotes (with '' escapes), and dollar-quoted strings / DO blocks (so
/// semicolons inside them don't split). A block comment whose first line is a request line becomes an
/// <see cref="HttpStep"/>; any other comment is ignored. A line starting with <c>\i</c>/<c>\ir</c> between
/// statements becomes an <see cref="IncludeStep"/> (expanded by <see cref="TestFileLoader"/>).
/// </summary>
public static class SqlTestFileParser
{
    private enum State { Normal, BlockComment, SingleQuote, DollarQuote }

    /// <summary>
    /// Parses one line as an include: <c>\i path</c> (cwd-relative) or <c>\ir path</c> (relative to the
    /// including file). The path may be single-quoted; a trailing ';' is forgiven. Returns false for any
    /// other line (including other backslash commands).
    /// </summary>
    internal static bool TryParseIncludeLine(string line, out string path, out bool relativeToFile)
    {
        path = "";
        relativeToFile = false;
        var t = line.Trim();
        bool relative = t.StartsWith("\\ir", StringComparison.OrdinalIgnoreCase)
            && (t.Length == 3 || char.IsWhiteSpace(t[3]));
        bool plain = !relative && t.StartsWith("\\i", StringComparison.OrdinalIgnoreCase)
            && (t.Length == 2 || char.IsWhiteSpace(t[2]));
        if (!relative && !plain) return false;

        var p = t[(relative ? 3 : 2)..].Trim();
        if (p.EndsWith(';')) p = p[..^1].TrimEnd();          // forgive a trailing ;
        if (p.Length > 1 && p[0] == '\'' && p[^1] == '\'')
        {
            p = p[1..^1];                                     // psql-style quoted path
        }
        if (p.Length == 0) return false;

        path = p;
        relativeToFile = relative;
        return true;
    }

    public static List<TestStep> Parse(string content)
    {
        var steps = new List<TestStep>();
        if (string.IsNullOrEmpty(content)) return steps;

        var state = State.Normal;
        var stmt = new StringBuilder();
        var dollarTag = new StringBuilder();
        var blockComment = new StringBuilder();
        int blockDepth = 0;
        int currentLine = 1;
        int stmtStartLine = 1;
        bool stmtHasContent = false;
        int blockCommentStartLine = 1;
        string lastToken = "";
        bool lastTokenIsWord = false;
        bool isDoBlock = false;

        void MarkContent()
        {
            if (!stmtHasContent)
            {
                stmtHasContent = true;
                stmtStartLine = currentLine;
            }
        }

        void FlushSql()
        {
            var text = stmt.ToString().Trim();
            if (text.Length > 0)
            {
                steps.Add(new SqlStep(text, isDoBlock, stmtStartLine));
            }
            stmt.Clear();
            stmtHasContent = false;
            isDoBlock = false;
            lastToken = "";
            lastTokenIsWord = false;
        }

        int i = 0;
        int len = content.Length;

        while (i < len)
        {
            char c = content[i];
            switch (state)
            {
                case State.Normal:
                    // include: \i <path> (cwd-relative) or \ir <path> (relative to the including file) —
                    // psql semantics. Recognized only between statements (pending statement is blank), so a
                    // backslash inside SQL text/strings/dollar-quotes is never misread. Consumes the line.
                    if (c == '\\' && !stmtHasContent)
                    {
                        int lineEnd = i;
                        while (lineEnd < len && content[lineEnd] != '\n') lineEnd++;
                        var includeLine = content[i..lineEnd].TrimEnd('\r').TrimEnd();
                        if (TryParseIncludeLine(includeLine, out var path, out var relative))
                        {
                            steps.Add(new IncludeStep(path, relative, currentLine));
                            i = lineEnd; // consume the include line; the newline is handled by the main loop
                            continue;
                        }
                        // Not \i/\ir: keep just the backslash as SQL text and let normal parsing continue
                        // (';' still splits; PostgreSQL will report the stray backslash clearly).
                        stmt.Append(c);
                        MarkContent();
                        lastToken = "";
                        lastTokenIsWord = false;
                        i++;
                        continue;
                    }
                    // line comment: -- (skip to end of line, never an HTTP step)
                    if (c == '-' && i + 1 < len && content[i + 1] == '-')
                    {
                        i += 2;
                        while (i < len && content[i] != '\n') i++;
                        continue;
                    }
                    // block comment: /*
                    if (c == '/' && i + 1 < len && content[i + 1] == '*')
                    {
                        state = State.BlockComment;
                        blockDepth = 1;
                        blockComment.Clear();
                        blockCommentStartLine = currentLine;
                        i += 2;
                        continue;
                    }
                    // single quote
                    if (c == '\'')
                    {
                        state = State.SingleQuote;
                        stmt.Append(c);
                        MarkContent();
                        lastToken = "";
                        lastTokenIsWord = false;
                        i++;
                        continue;
                    }
                    // dollar quote: $tag$ or $$
                    if (c == '$')
                    {
                        int tagStart = i;
                        i++;
                        while (i < len && (char.IsLetterOrDigit(content[i]) || content[i] == '_')) i++;
                        if (i < len && content[i] == '$')
                        {
                            dollarTag.Clear();
                            dollarTag.Append(content, tagStart, i - tagStart + 1);
                            state = State.DollarQuote;
                            if (lastTokenIsWord && lastToken.Length == 2
                                && (lastToken[0] is 'd' or 'D') && (lastToken[1] is 'o' or 'O'))
                            {
                                isDoBlock = true;
                            }
                            stmt.Append(content, tagStart, i - tagStart + 1);
                            MarkContent();
                            lastToken = "";
                            lastTokenIsWord = false;
                            i++;
                            continue;
                        }
                        // not a valid dollar quote: treat $ + run as normal text
                        stmt.Append(content, tagStart, i - tagStart);
                        MarkContent();
                        lastToken = "";
                        lastTokenIsWord = false;
                        continue;
                    }
                    // statement separator
                    if (c == ';')
                    {
                        FlushSql();
                        i++;
                        continue;
                    }
                    // word token (for DO-block detection)
                    if (char.IsLetter(c) || c == '_')
                    {
                        int ws = i;
                        while (i < len && (char.IsLetterOrDigit(content[i]) || content[i] == '_')) i++;
                        stmt.Append(content, ws, i - ws);
                        MarkContent();
                        lastToken = content.Substring(ws, i - ws);
                        lastTokenIsWord = true;
                        continue;
                    }
                    // whitespace / other
                    if (c == '\n') currentLine++;
                    stmt.Append(c);
                    if (!char.IsWhiteSpace(c))
                    {
                        MarkContent();
                        lastToken = "";
                        lastTokenIsWord = false;
                    }
                    i++;
                    break;

                case State.BlockComment:
                    if (c == '/' && i + 1 < len && content[i + 1] == '*')
                    {
                        blockDepth++;
                        blockComment.Append("/*");
                        i += 2;
                        continue;
                    }
                    if (c == '*' && i + 1 < len && content[i + 1] == '/')
                    {
                        blockDepth--;
                        if (blockDepth == 0)
                        {
                            state = State.Normal;
                            i += 2;
                            var http = HttpFileRequestParser.TryParse(blockComment.ToString(), blockCommentStartLine);
                            if (http is not null)
                            {
                                FlushSql(); // emit any pending SQL before the HTTP step
                                steps.Add(http);
                            }
                            // else: ordinary comment → ignored
                            continue;
                        }
                        blockComment.Append("*/");
                        i += 2;
                        continue;
                    }
                    if (c == '\n') currentLine++;
                    blockComment.Append(c);
                    i++;
                    break;

                case State.SingleQuote:
                    stmt.Append(c);
                    if (c == '\n') currentLine++;
                    if (c == '\'')
                    {
                        if (i + 1 < len && content[i + 1] == '\'')
                        {
                            stmt.Append('\'');
                            i += 2;
                            continue;
                        }
                        state = State.Normal;
                    }
                    i++;
                    break;

                case State.DollarQuote:
                    stmt.Append(c);
                    if (c == '\n') currentLine++;
                    if (c == '$' && i + dollarTag.Length <= len)
                    {
                        bool match = true;
                        for (int j = 0; j < dollarTag.Length; j++)
                        {
                            if (content[i + j] != dollarTag[j]) { match = false; break; }
                        }
                        if (match)
                        {
                            stmt.Append(content, i + 1, dollarTag.Length - 1);
                            i += dollarTag.Length;
                            state = State.Normal;
                            continue;
                        }
                    }
                    i++;
                    break;
            }
        }

        FlushSql();
        return steps;
    }
}
