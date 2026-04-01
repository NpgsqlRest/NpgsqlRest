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
    /// Virtual parameters from @define_param annotations.
    /// Each entry is (name, type) where type may be null (defaults to text).
    /// </summary>
    public List<(string Name, string? Type)> VirtualParams { get; } = [];

    /// <summary>
    /// Per-command @single annotations (positional).
    /// Key is 0-based statement index, value is true if @single was placed before that statement.
    /// </summary>
    public HashSet<int> SingleCommands { get; } = [];

    /// <summary>
    /// Per-command @result annotations (positional).
    /// Key is 0-based statement index, value is the custom result name.
    /// </summary>
    public Dictionary<int, string> PositionalResultNames { get; } = [];

    /// <summary>
    /// Per-command @skip annotations (positional).
    /// Contains 0-based statement indices of commands that should execute but not produce result keys.
    /// </summary>
    public HashSet<int> SkipCommands { get; } = [];

    /// <summary>
    /// Per-command @returns annotations (positional).
    /// Key is 0-based statement index, value is the composite type name.
    /// When present, the Describe step is skipped for that statement and
    /// columns are resolved from the composite type metadata instead.
    /// </summary>
    public Dictionary<int, string> ReturnsTypeOverrides { get; } = [];

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
        // Track comments between statements for positional per-command annotations
        var interStmtCommentBuilder = new ValueStringBuilder(stackalloc char[256]);
        // Track header comment length (before first statement) for per-command annotation extraction
        int headerCommentLength = -1;
        // Track whether we're still on the same line as the last semicolon (for inline annotations)
        bool sameLineAsSemicolon = false;
        // Track whether a block comment started on the same line as a semicolon
        bool blockCommentSameLine = false;
        var blockCommentBuffer = new ValueStringBuilder(stackalloc char[128]);

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
                            // Collect inter-statement comments for positional annotations
                            if (firstStatementSeen)
                            {
                                if (sameLineAsSemicolon)
                                {
                                    // Comment on same line as ; → applies to the just-completed statement
                                    var prevIndex = result.Statements.Count - 1;
                                    if (prevIndex >= 0)
                                    {
                                        ExtractPerCommandAnnotations(content[commentStart..i].ToString(), prevIndex, result);
                                    }
                                }
                                else
                                {
                                    if (interStmtCommentBuilder.Length > 0) interStmtCommentBuilder.Append('\n');
                                    interStmtCommentBuilder.Append(content[commentStart..i]);
                                }
                            }
                            sameLineAsSemicolon = false;
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
                            blockCommentSameLine = sameLineAsSemicolon;
                            i += 2;
                            if (ShouldCollectComment(commentScope, firstStatementSeen))
                            {
                                if (commentBuilder.Length > 0) commentBuilder.Append('\n');
                            }
                            if (firstStatementSeen && !blockCommentSameLine)
                            {
                                if (interStmtCommentBuilder.Length > 0) interStmtCommentBuilder.Append('\n');
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
                                // Before adding this statement, extract per-command annotations
                                // from comments collected between the previous statement and this one
                                if (firstStatementSeen && interStmtCommentBuilder.Length > 0)
                                {
                                    var nextIndex = result.Statements.Count; // 0-based index of the statement about to be added
                                    ExtractPerCommandAnnotations(interStmtCommentBuilder.ToString(), nextIndex, result);
                                    interStmtCommentBuilder.Clear();
                                }
                                if (!firstStatementSeen)
                                {
                                    headerCommentLength = commentBuilder.Length;
                                }
                                result.Statements.Add(stmt);
                                firstStatementSeen = true;
                            }
                            stmtBuilder.Clear();
                            lastTokenIsWord = false;
                            lastToken.Clear();
                            sameLineAsSemicolon = true;
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
                            if (c == '\n') sameLineAsSemicolon = false;
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
                                if (blockCommentSameLine && firstStatementSeen && blockCommentBuffer.Length > 0)
                                {
                                    var prevIndex = result.Statements.Count - 1;
                                    if (prevIndex >= 0)
                                    {
                                        ExtractPerCommandAnnotations(blockCommentBuffer.ToString(), prevIndex, result);
                                    }
                                    blockCommentBuffer.Clear();
                                }
                                blockCommentSameLine = false;
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
                        if (firstStatementSeen)
                        {
                            if (blockCommentSameLine)
                                blockCommentBuffer.Append(c);
                            else
                                interStmtCommentBuilder.Append(c);
                        }
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
                if (firstStatementSeen && interStmtCommentBuilder.Length > 0)
                {
                    var nextIndex = result.Statements.Count;
                    ExtractPerCommandAnnotations(interStmtCommentBuilder.ToString(), nextIndex, result);
                    interStmtCommentBuilder.Clear();
                }
                result.Statements.Add(lastStmt);
            }

            result.Comment = commentBuilder.ToString();

            // Extract positional per-command annotations from header comments for the first command
            if (result.Statements.Count > 1 && headerCommentLength > 0)
            {
                ExtractPerCommandAnnotations(result.Comment[..headerCommentLength], 0, result);
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
            interStmtCommentBuilder.Dispose();
            blockCommentBuffer.Dispose();
        }
    }

    private static bool ShouldCollectComment(CommentScope scope, bool firstStatementSeen)
    {
        return scope == CommentScope.All || (scope == CommentScope.Header && !firstStatementSeen);
    }

    /// <summary>
    /// Extract positional per-command annotations from comments between statements.
    /// Supports: @single, @skip, @result name, @result is name
    /// </summary>
    private static void ExtractPerCommandAnnotations(string comment, int statementIndex, SqlFileParseResult result)
    {
        var lines = comment.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            var s = trimmed.StartsWith('@') ? trimmed[1..] : trimmed;

            // @single / @single_record / @single_result
            if (s.Equals("single", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("single_record", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("single_result", StringComparison.OrdinalIgnoreCase))
            {
                result.SingleCommands.Add(statementIndex);
                continue;
            }

            // @skip / @skip_result / @no_result
            if (s.Equals("skip", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("skip_result", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("no_result", StringComparison.OrdinalIgnoreCase))
            {
                result.SkipCommands.Add(statementIndex);
                continue;
            }

            // @returns type_name (positional — skip Describe, use composite type columns)
            if (s.StartsWith("returns", StringComparison.OrdinalIgnoreCase) && s.Length > 7)
            {
                var typeName = s[7..].TrimStart();
                if (typeName.Length > 0)
                {
                    result.ReturnsTypeOverrides[statementIndex] = typeName;
                }
                continue;
            }

            // @result name / @result is name (positional)
            if (s.StartsWith("result", StringComparison.OrdinalIgnoreCase) && s.Length > 6)
            {
                var afterResult = s[6..].TrimStart();
                if (afterResult.Length == 0) continue;

                var rest = afterResult;

                var parts = rest.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                string? name = null;

                if (parts.Length >= 2 && string.Equals(parts[0], "is", StringComparison.OrdinalIgnoreCase))
                {
                    name = parts[1];
                }
                else if (parts.Length >= 1)
                {
                    name = parts[0];
                }

                if (name is not null)
                {
                    result.PositionalResultNames[statementIndex] = name;
                }
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

    /// <summary>
    /// Extract parameter type hints from @param annotations.
    /// Looks for patterns like: @param $1 name type, @param $1 is name type
    /// Only extracts when a positional $N and an explicit type are present.
    /// Returns null if no type hints found.
    /// </summary>
    public static Dictionary<int, string>? ExtractParamTypeHints(string? comment)
    {
        if (string.IsNullOrEmpty(comment)) return null;

        Dictionary<int, string>? hints = null;
        var lines = comment.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            var s = trimmed.StartsWith('@') ? trimmed[1..] : trimmed;

            if (!s.StartsWith("param ", StringComparison.OrdinalIgnoreCase) &&
                !s.StartsWith("parameter ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = s.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2 || parts[1][0] != '$')
            {
                continue;
            }

            if (!int.TryParse(parts[1].AsSpan(1), out int paramIndex) || paramIndex < 1)
            {
                continue;
            }

            // @param $1 name type ... → parts[3] is type candidate
            // @param $1 is name type ... → parts[4] is type candidate
            string? typeName = null;
            if (parts.Length >= 4 && parts[2].Equals("is", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length >= 5)
                {
                    var candidate = parts[4].ToLowerInvariant();
                    if (candidate != "default" && candidate != "=")
                    {
                        typeName = candidate;
                    }
                }
            }
            else if (parts.Length >= 4)
            {
                var candidate = parts[3].ToLowerInvariant();
                if (candidate != "default" && candidate != "=")
                {
                    typeName = candidate;
                }
            }

            if (typeName is not null)
            {
                var descriptor = new NpgsqlRest.TypeDescriptor(typeName);
                if (descriptor.DbType != NpgsqlTypes.NpgsqlDbType.Unknown)
                {
                    hints ??= [];
                    hints[paramIndex - 1] = typeName;
                }
            }
        }

        return hints;
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
