using System.Text;

namespace NpgsqlRestClient.Testing;

/// <summary>
/// Splits a .test.sql file into an ordered list of steps (SQL statements and embedded HTTP requests),
/// preserving interleaving. A char-by-char state machine handles ';' statement splitting, line/block
/// comments, single quotes (with '' escapes), and dollar-quoted strings / DO blocks (so semicolons inside
/// them don't split). A block comment whose first line is a request line becomes an <see cref="HttpStep"/>;
/// any other comment is ignored.
/// </summary>
public static class SqlTestFileParser
{
    private enum State { Normal, BlockComment, SingleQuote, DollarQuote }

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
