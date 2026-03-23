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
    /// Per-statement command names from @command_name annotations.
    /// Index matches Statements index. Null entries use default naming.
    /// </summary>
    public List<string?> CommandNames { get; } = [];

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

        // Extract @command_name annotations from the comment text per statement
        if (result.Statements.Count > 1)
        {
            ExtractCommandNames(result);
        }

        commentBuilder.Dispose();
        stmtBuilder.Dispose();
        dollarTag.Dispose();
        lastToken.Dispose();

        return result;
    }

    private static bool ShouldCollectComment(CommentScope scope, bool firstStatementSeen)
    {
        return scope == CommentScope.All || (scope == CommentScope.Header && !firstStatementSeen);
    }

    /// <summary>
    /// Extract @command_name annotations from the comment text.
    /// Each @command_name applies to the next statement.
    /// </summary>
    private static void ExtractCommandNames(SqlFileParseResult result)
    {
        // Parse @command_name from comment lines
        // The comment text is already extracted. We scan for lines containing @command_name
        // and map them to statement indices based on their order of appearance.
        var lines = result.Comment.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        var commandNameQueue = new Queue<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("@command_name ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("command_name ", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    commandNameQueue.Enqueue(parts[1]);
                }
            }
        }

        // Assign command names to statements in order
        for (int i = 0; i < result.Statements.Count; i++)
        {
            result.CommandNames.Add(commandNameQueue.Count > 0 ? commandNameQueue.Dequeue() : null);
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
