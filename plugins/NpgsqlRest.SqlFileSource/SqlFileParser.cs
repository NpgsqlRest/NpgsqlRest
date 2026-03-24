namespace NpgsqlRest.SqlFileSource;

/// <summary>
/// Result of parsing a SQL file.
/// </summary>
public class SqlFileParseResult
{
    /// <summary>
    /// Extracted comment text (all comments concatenated, markers stripped).
    /// Fed to DefaultCommentParser for annotation processing.
    /// </summary>
    public string Comment { get; set; } = "";

    /// <summary>
    /// Individual SQL statements (split on ; outside strings/quotes/comments).
    /// </summary>
    public List<string> Statements { get; } = [];

    /// <summary>
    /// Whether a mutation command (INSERT, UPDATE, DELETE) was detected outside strings/quotes/comments.
    /// </summary>
    public bool HasInsert { get; set; }
    public bool HasUpdate { get; set; }
    public bool HasDelete { get; set; }

    /// <summary>
    /// Whether a DO block was detected.
    /// </summary>
    public bool IsDoBlock { get; set; }

    /// <summary>
    /// Result key overrides from @resultN annotations (e.g., @result1 validate).
    /// Key is 1-based result index, value is the custom name.
    /// </summary>
    public Dictionary<int, string> ResultNames { get; } = [];

    /// <summary>
    /// Virtual parameters from @define_param annotations.
    /// Each entry is (name, type) where type may be null (defaults to text).
    /// </summary>
    public List<(string Name, string? Type)> VirtualParams { get; } = [];

    /// <summary>
    /// Errors encountered during parsing.
    /// </summary>
    public List<string> Errors { get; } = [];

    /// <summary>
    /// Auto-detected HTTP method based on mutations.
    /// DELETE > POST (UPDATE) > PUT (INSERT) > GET (none).
    /// </summary>
    public Method AutoHttpMethod
    {
        get
        {
            if (IsDoBlock) return Method.POST;
            if (HasDelete) return Method.DELETE;
            if (HasUpdate) return Method.POST;
            if (HasInsert) return Method.PUT;
            return Method.GET;
        }
    }
}

/// <summary>
/// Single-pass SQL file parser. Extracts comments and splits statements simultaneously.
/// Handles: line comments (--), block comments (/* */), single-quoted strings (''),
/// dollar-quoted strings ($$...$$, $tag$...$tag$), and semicolon statement splitting.
/// Also detects mutation commands and DO blocks outside quoted/commented regions.
/// </summary>
public static class SqlFileParser
{
    private enum State
    {
        Normal,
        LineComment,
        BlockComment,
        SingleQuote,
        DollarQuote,
    }

    /// <summary>
    /// Parse a SQL file content into comments and statements.
    /// </summary>
    /// <param name="content">The SQL file content.</param>
    /// <param name="commentScope">Which comments to extract as annotations.</param>
    public static SqlFileParseResult Parse(ReadOnlySpan<char> content, CommentScope commentScope = CommentScope.All)
    {
        var result = new SqlFileParseResult();
        if (content.IsEmpty) return result;

        var state = State.Normal;
        var commentBuilder = new ValueStringBuilder(stackalloc char[512]);
        var stmtBuilder = new ValueStringBuilder(stackalloc char[Math.Min(content.Length, 4096)]);
        var dollarTag = new ValueStringBuilder(stackalloc char[64]);
        int blockCommentDepth = 0;
        bool firstStatementSeen = false;
        // Track the token before a dollar-quote for DO block detection
        var lastToken = new ValueStringBuilder(stackalloc char[16]);
        bool lastTokenIsWord = false;

        int i = 0;
        int len = content.Length;

        try
        {
            while (i < len)
            {
                char c = content[i];

                switch (state)
                {
                    case State.Normal:
                        // Check for line comment: --
                        if (c == '-' && i + 1 < len && content[i + 1] == '-')
                        {
                            // Extract comment text until end of line
                            i += 2;
                            int commentStart = i;
                            while (i < len && content[i] != '\n' && content[i] != '\r')
                                i++;

                            if (ShouldCollectComment(commentScope, firstStatementSeen))
                            {
                                if (commentBuilder.Length > 0) commentBuilder.Append('\n');
                                commentBuilder.Append(content[commentStart..i]);
                            }
                            // Skip \r\n
                            if (i < len && content[i] == '\r') i++;
                            if (i < len && content[i] == '\n') i++;
                            continue;
                        }

                        // Check for block comment: /*
                        if (c == '/' && i + 1 < len && content[i + 1] == '*')
                        {
                            state = State.BlockComment;
                            blockCommentDepth = 1;
                            i += 2;
                            if (ShouldCollectComment(commentScope, firstStatementSeen))
                            {
                                if (commentBuilder.Length > 0) commentBuilder.Append('\n');
                            }
                            continue;
                        }

                        // Check for single quote: '
                        if (c == '\'')
                        {
                            state = State.SingleQuote;
                            stmtBuilder.Append(c);
                            lastTokenIsWord = false;
                            lastToken.Clear();
                            i++;
                            continue;
                        }

                        // Check for dollar quote: $tag$  or $$
                        if (c == '$')
                        {
                            int tagStart = i;
                            i++;
                            // Collect tag name (alphanumeric + underscore)
                            while (i < len && (char.IsLetterOrDigit(content[i]) || content[i] == '_'))
                                i++;
                            if (i < len && content[i] == '$')
                            {
                                // Valid dollar quote opening
                                dollarTag.Clear();
                                dollarTag.Append(content[tagStart..(i + 1)]);
                                state = State.DollarQuote;

                                // Check if the last token before this dollar-quote was DO
                                if (lastTokenIsWord && lastToken.Length == 2 &&
                                    char.ToUpperInvariant(lastToken[0]) == 'D' &&
                                    char.ToUpperInvariant(lastToken[1]) == 'O')
                                {
                                    result.IsDoBlock = true;
                                }

                                // Append the dollar-quote opening to statement
                                stmtBuilder.Append(content[tagStart..(i + 1)]);
                                lastTokenIsWord = false;
                                lastToken.Clear();
                                i++;
                                continue;
                            }
                            else
                            {
                                // Not a valid dollar quote, treat $ as normal char
                                stmtBuilder.Append(content[tagStart..i]);
                                // Don't increment i — we've already advanced past the $+tag chars
                                continue;
                            }
                        }

                        // Check for semicolon: statement separator
                        if (c == ';')
                        {
                            var stmt = stmtBuilder.ToString().Trim();
                            if (stmt.Length > 0)
                            {
                                result.Statements.Add(stmt);
                                firstStatementSeen = true;
                            }
                            stmtBuilder.Clear();
                            lastTokenIsWord = false;
                            lastToken.Clear();
                            i++;
                            continue;
                        }

                        // Track tokens for mutation detection and DO block detection
                        if (char.IsLetter(c) || c == '_')
                        {
                            int wordStart = i;
                            while (i < len && (char.IsLetterOrDigit(content[i]) || content[i] == '_'))
                                i++;

                            var word = content[wordStart..i];
                            stmtBuilder.Append(word);

                            // Track last token for DO detection
                            lastToken.Clear();
                            lastToken.Append(word);
                            lastTokenIsWord = true;

                            // Mutation detection (case-insensitive)
                            DetectMutation(word, result);
                            continue;
                        }

                        // Whitespace and other characters
                        if (char.IsWhiteSpace(c))
                        {
                            stmtBuilder.Append(c);
                            // Don't clear lastToken on whitespace — DO $$ has space between
                        }
                        else
                        {
                            stmtBuilder.Append(c);
                            lastTokenIsWord = false;
                            lastToken.Clear();
                        }
                        i++;
                        break;

                    // ReSharper disable once UnreachableSwitchCaseDueToIntegerAnalysis
                    case State.LineComment:
                        // Shouldn't reach here — line comments handled inline above
                        i++;
                        break;

                    case State.BlockComment:
                        if (c == '/' && i + 1 < len && content[i + 1] == '*')
                        {
                            blockCommentDepth++;
                            if (ShouldCollectComment(commentScope, firstStatementSeen))
                                commentBuilder.Append("/*");
                            i += 2;
                            continue;
                        }
                        if (c == '*' && i + 1 < len && content[i + 1] == '/')
                        {
                            blockCommentDepth--;
                            if (blockCommentDepth == 0)
                            {
                                state = State.Normal;
                                i += 2;
                                continue;
                            }
                            if (ShouldCollectComment(commentScope, firstStatementSeen))
                                commentBuilder.Append("*/");
                            i += 2;
                            continue;
                        }
                        if (ShouldCollectComment(commentScope, firstStatementSeen))
                            commentBuilder.Append(c);
                        i++;
                        break;

                    case State.SingleQuote:
                        stmtBuilder.Append(c);
                        if (c == '\'')
                        {
                            // Check for escaped quote ''
                            if (i + 1 < len && content[i + 1] == '\'')
                            {
                                stmtBuilder.Append('\'');
                                i += 2;
                                continue;
                            }
                            state = State.Normal;
                        }
                        i++;
                        break;

                    case State.DollarQuote:
                        stmtBuilder.Append(c);
                        // Check if we're at the closing dollar-quote tag
                        if (c == '$' && i + dollarTag.Length - 1 <= len)
                        {
                            var candidate = content[i..(i + dollarTag.Length)];
                            bool match = true;
                            for (int j = 0; j < dollarTag.Length; j++)
                            {
                                if (candidate[j] != dollarTag[j])
                                {
                                    match = false;
                                    break;
                                }
                            }
                            if (match)
                            {
                                // Append rest of closing tag (we already appended c which is $)
                                stmtBuilder.Append(content[(i + 1)..(i + dollarTag.Length)]);
                                i += dollarTag.Length;
                                state = State.Normal;
                                continue;
                            }
                        }
                        i++;
                        break;
                }
            }

            // Handle remaining statement (no trailing semicolon)
            var lastStmt = stmtBuilder.ToString().Trim();
            if (lastStmt.Length > 0)
            {
                result.Statements.Add(lastStmt);
            }

            result.Comment = commentBuilder.ToString();

            // Extract @resultN annotations from the comment text for multi-command files
            if (result.Statements.Count > 1)
            {
                ExtractResultNames(result);
            }

            // Extract @define_param annotations
            ExtractVirtualParams(result);

            return result;
        }
        finally
        {
            commentBuilder.Dispose();
            stmtBuilder.Dispose();
            dollarTag.Dispose();
            lastToken.Dispose();
        }
    }

    private static bool ShouldCollectComment(CommentScope scope, bool firstStatementSeen)
    {
        return scope == CommentScope.All || (scope == CommentScope.Header && !firstStatementSeen);
    }

    /// <summary>
    /// Extract @resultN annotations from comment text.
    /// Supports: @result1 name, @result1 is name
    /// The number after "result" is the 1-based index.
    /// </summary>
    private static void ExtractResultNames(SqlFileParseResult result)
    {
        var lines = result.Comment.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            // Strip optional @ prefix
            var s = trimmed.StartsWith('@') ? trimmed[1..] : trimmed;

            // Match "resultN ..." where N is one or more digits
            if (!s.StartsWith("result", StringComparison.OrdinalIgnoreCase) || s.Length < 7)
            {
                continue;
            }

            // Extract the number after "result"
            int numStart = 6; // length of "result"
            int numEnd = numStart;
            while (numEnd < s.Length && char.IsDigit(s[numEnd]))
            {
                numEnd++;
            }
            if (numEnd == numStart)
            {
                continue; // no digits after "result"
            }

            if (!int.TryParse(s[numStart..numEnd], out int resultIndex) || resultIndex < 1)
            {
                continue;
            }

            // Rest after the number: " name" or " is name"
            var rest = s[numEnd..].Trim();
            if (rest.Length == 0)
            {
                continue;
            }

            var parts = rest.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            string? name = null;

            if (parts.Length >= 2 && string.Equals(parts[0], "is", StringComparison.OrdinalIgnoreCase))
            {
                // @resultN is name
                name = parts[1];
            }
            else if (parts.Length >= 1)
            {
                // @resultN name
                name = parts[0];
            }

            if (name is not null)
            {
                result.ResultNames[resultIndex] = name;
            }
        }
    }

    /// <summary>
    /// Extract @define_param annotations from comment text.
    /// Supports: @define_param name, @define_param name type
    /// </summary>
    private static void ExtractVirtualParams(SqlFileParseResult result)
    {
        if (string.IsNullOrEmpty(result.Comment)) return;

        var lines = result.Comment.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            var s = trimmed.StartsWith('@') ? trimmed[1..] : trimmed;

            if (!s.StartsWith("define_param", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = s.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue; // no name provided
            }

            var name = parts[1];
            var type = parts.Length >= 3 ? parts[2] : null;
            result.VirtualParams.Add((name, type));
        }
    }

    private static void DetectMutation(ReadOnlySpan<char> word, SqlFileParseResult result)
    {
        if (word.Length == 6 &&
            (word[0] == 'I' || word[0] == 'i') &&
            (word[1] == 'N' || word[1] == 'n') &&
            (word[2] == 'S' || word[2] == 's') &&
            (word[3] == 'E' || word[3] == 'e') &&
            (word[4] == 'R' || word[4] == 'r') &&
            (word[5] == 'T' || word[5] == 't'))
        {
            result.HasInsert = true;
        }
        else if (word.Length == 6 &&
            (word[0] == 'U' || word[0] == 'u') &&
            (word[1] == 'P' || word[1] == 'p') &&
            (word[2] == 'D' || word[2] == 'd') &&
            (word[3] == 'A' || word[3] == 'a') &&
            (word[4] == 'T' || word[4] == 't') &&
            (word[5] == 'E' || word[5] == 'e'))
        {
            result.HasUpdate = true;
        }
        else if (word.Length == 6 &&
            (word[0] == 'D' || word[0] == 'd') &&
            (word[1] == 'E' || word[1] == 'e') &&
            (word[2] == 'L' || word[2] == 'l') &&
            (word[3] == 'E' || word[3] == 'e') &&
            (word[4] == 'T' || word[4] == 't') &&
            (word[5] == 'E' || word[5] == 'e'))
        {
            result.HasDelete = true;
        }
    }
}

/// <summary>
/// Stack-allocated string builder for efficient parsing without heap allocations during scanning.
/// </summary>
internal ref struct ValueStringBuilder
{
    private Span<char> _buffer;
    private char[]? _arrayFromPool;
    private int _pos;

    public ValueStringBuilder(Span<char> initialBuffer)
    {
        _buffer = initialBuffer;
        _arrayFromPool = null;
        _pos = 0;
    }

    public int Length => _pos;

    public char this[int index] => _buffer[index];

    public void Append(char c)
    {
        if (_pos >= _buffer.Length)
            Grow(1);
        _buffer[_pos++] = c;
    }

    public void Append(ReadOnlySpan<char> value)
    {
        if (_pos + value.Length > _buffer.Length)
            Grow(value.Length);
        value.CopyTo(_buffer[_pos..]);
        _pos += value.Length;
    }

    public void Append(string value) => Append(value.AsSpan());

    public void Clear() => _pos = 0;

    public override string ToString() => _buffer[.._pos].ToString();

    private void Grow(int additionalCapacity)
    {
        int newCapacity = Math.Max(_buffer.Length * 2, _buffer.Length + additionalCapacity);
        var newArray = System.Buffers.ArrayPool<char>.Shared.Rent(newCapacity);
        _buffer[.._pos].CopyTo(newArray);
        if (_arrayFromPool is not null)
            System.Buffers.ArrayPool<char>.Shared.Return(_arrayFromPool);
        _arrayFromPool = newArray;
        _buffer = newArray;
    }

    public void Dispose()
    {
        if (_arrayFromPool is not null)
        {
            System.Buffers.ArrayPool<char>.Shared.Return(_arrayFromPool);
            _arrayFromPool = null;
        }
    }
}
