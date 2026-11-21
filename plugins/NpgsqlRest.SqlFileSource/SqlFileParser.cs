using System.Text;
using NpgsqlRest.Common;

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
    /// The statements whose @returns was written WITHOUT the @ prefix. Such a line may be a sentence that happens
    /// to look like the annotation ("Returns users"), so a type that does not exist is not an error for these:
    /// the line is taken as a comment and the statement is described as usual.
    /// </summary>
    public HashSet<int> ImplicitReturns { get; } = [];

    /// <summary>
    /// The comment lines (trimmed, as written) that this parser read as annotations of its own:
    /// skip, returns, result, define_param. They are not part of the endpoint's description.
    /// </summary>
    public HashSet<string> ConsumedCommentLines { get; } = new(StringComparer.Ordinal);

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
/// Result of the named-parameter (:name) rewrite pass (<see cref="SqlFileParser.RewriteNamedParameters"/>).
/// </summary>
public sealed class NamedParamRewriteResult
{
    /// <summary>
    /// Placeholder names in first-appearance order; index i corresponds to $(i+1) in the rewritten SQL.
    /// The same name (case-insensitive) always maps to the same ordinal, file-wide.
    /// </summary>
    public List<string> Names { get; } = [];

    /// <summary>The file uses :name placeholders.</summary>
    public bool HasNamed => Names.Count > 0;

    /// <summary>A native positional ($N) placeholder was found outside strings/comments/dollar-quotes.</summary>
    public bool HasPositional { get; set; }
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
    // RequireAtPrefix for the Parse that is running on this thread: a comment line is an annotation only with the @.
    [ThreadStatic] private static bool _requireAt;

    public static SqlFileParseResult Parse(ReadOnlySpan<char> content, CommentScope commentScope = CommentScope.All, bool requireAtPrefix = false)
    {
        _requireAt = requireAtPrefix;
        var result = new SqlFileParseResult();
        if (content.IsEmpty) return result;

        var state = State.Normal;
        var commentBuilder = new ValueStringBuilder(stackalloc char[512]);
        var stmtBuilder = new ValueStringBuilder(stackalloc char[Math.Min(content.Length, 4096)]);
        var dollarTag = new ValueStringBuilder(stackalloc char[64]);
        int blockCommentDepth = 0;
        // the string being read is E'...': a backslash escapes the next character
        bool escapeString = false;
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

                        // A quoted identifier: "..." (a doubled "" inside it is a quote). Nothing in it is a comment,
                        // a string or the end of a statement: select 1 as "a--b; c".
                        if (c == '"')
                        {
                            int identifierStart = i;
                            i++;
                            while (i < len)
                            {
                                if (content[i] == '"')
                                {
                                    if (i + 1 < len && content[i + 1] == '"')
                                    {
                                        i += 2;
                                        continue;
                                    }
                                    i++;
                                    break;
                                }
                                i++;
                            }
                            stmtBuilder.Append(content[identifierStart..i]);
                            lastTokenIsWord = false;
                            lastToken.Clear();
                            continue;
                        }

                        // Check for single quote: '
                        if (c == '\'')
                        {
                            // E'...' (an escape string): a backslash escapes the next character, also a quote
                            escapeString = i > 0 && (content[i - 1] is 'E' or 'e')
                                && (i == 1 || !(char.IsLetterOrDigit(content[i - 2]) || content[i - 2] == '_'));
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
                            // A tag is an identifier and cannot begin with a digit: in "$1$2" the first dollar is the
                            // parameter $1, not the opening of a string tagged "1".
                            if (i < len && content[i] == '$' && !(i > tagStart + 1 && char.IsDigit(content[tagStart + 1])))
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
                        if (escapeString && c == '\\' && i + 1 < len)
                        {
                            stmtBuilder.Append(content[i + 1]);
                            i += 2;
                            continue;
                        }
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
                if (!firstStatementSeen)
                {
                    // one statement with no semicolon: everything collected so far is its header
                    headerCommentLength = commentBuilder.Length;
                }
                result.Statements.Add(lastStmt);
            }

            result.Comment = commentBuilder.ToString();

            // Extract positional per-command annotations from header comments for the first command
            if (headerCommentLength > 0)
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
    /// Supports: @single, @skip, @returns type, @result name, @result is name.
    ///
    /// A line is an annotation when its first WORD is the keyword (so "resulting rows" and "returns_x" are not).
    /// The @ prefix says the line is meant as an annotation. Without it, a comment may simply be a sentence that
    /// starts with one of these words ("Returns all active users", "result of the join"), so such a line is taken
    /// as an annotation only when the rest of it has the shape of one: a single name, or a type name.
    /// </summary>
    private static void ExtractPerCommandAnnotations(string comment, int statementIndex, SqlFileParseResult result)
    {
        var lines = comment.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            var explicitAnnotation = trimmed.StartsWith('@');
            if (_requireAt && explicitAnnotation is false)
            {
                continue; // RequireAtPrefix: text
            }
            var s = explicitAnnotation ? trimmed[1..] : trimmed;

            // @single / @single_record / @single_result
            if (CommentPrimitives.StrEqualsToArray(s, "single", "single_record", "single_result"))
            {
                result.SingleCommands.Add(statementIndex);
                continue;
            }

            // @skip / @skip_result / @no_result
            if (CommentPrimitives.StrEqualsToArray(s, "skip", "skip_result", "no_result"))
            {
                result.SkipCommands.Add(statementIndex);
                result.ConsumedCommentLines.Add(trimmed);
                continue;
            }

            // @returns type_name (positional — skip Describe, use composite type columns)
            if (TryKeyword(s, "returns", out var typeName))
            {
                typeName = typeName.TrimEnd(';').Trim();
                if (typeName.Length == 0)
                {
                    continue;
                }
                if (explicitAnnotation is false && LooksLikeTypeName(typeName) is false)
                {
                    continue; // a sentence, not an annotation
                }
                result.ReturnsTypeOverrides[statementIndex] = typeName;
                if (explicitAnnotation is false)
                {
                    result.ImplicitReturns.Add(statementIndex);
                }
                result.ConsumedCommentLines.Add(trimmed);
                continue;
            }

            // @result name / @result is name (positional)
            if (TryKeyword(s, "result", out var afterResult))
            {
                var parts = afterResult.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                string? name = null;

                if (parts.Length >= 2 && string.Equals(parts[0], "is", StringComparison.OrdinalIgnoreCase))
                {
                    name = parts[1];
                    if (explicitAnnotation is false && parts.Length > 2)
                    {
                        continue; // "result is not what ..." - a sentence
                    }
                }
                else if (parts.Length >= 1)
                {
                    name = parts[0];
                    if (explicitAnnotation is false && parts.Length > 1)
                    {
                        continue; // "result of the join" - a sentence
                    }
                }

                if (name is not null && (explicitAnnotation || IsSimpleName(name)))
                {
                    result.PositionalResultNames[statementIndex] = name;
                    result.ConsumedCommentLines.Add(trimmed);
                }
            }
        }
    }

    /// <summary>
    /// True when <paramref name="line"/> starts with the word <paramref name="keyword"/> (case-insensitive) followed
    /// by whitespace; <paramref name="rest"/> is what follows, trimmed. "returns" alone, "returns_x" and
    /// "resulting" do not match.
    /// </summary>
    internal static bool TryKeyword(string line, string keyword, out string rest)
    {
        rest = "";
        if (line.Length <= keyword.Length ||
            line.StartsWith(keyword, StringComparison.OrdinalIgnoreCase) is false ||
            char.IsWhiteSpace(line[keyword.Length]) is false)
        {
            return false;
        }
        rest = line[(keyword.Length + 1)..].Trim();
        return true;
    }

    private static bool IsSimpleName(string value)
    {
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c) is false && c != '_')
            {
                return false;
            }
        }
        return value.Length > 0;
    }

    private static readonly string[] MultiWordTypes =
    [
        "double precision", "character varying", "bit varying",
        "timestamp with time zone", "timestamp without time zone",
        "time with time zone", "time without time zone"
    ];

    /// <summary>
    /// True when the text has the shape of a type name: one token (my_schema.my_type, int[], numeric(10,2),
    /// "Quoted Name"), or one of PostgreSQL's type names of several words (double precision,
    /// timestamp with time zone ...), with an optional length and array suffix.
    /// </summary>
    internal static bool LooksLikeTypeName(string value)
    {
        value = value.Trim();
        if (value.Length == 0)
        {
            return false;
        }
        foreach (var multi in MultiWordTypes)
        {
            if (value.StartsWith(multi, StringComparison.OrdinalIgnoreCase))
            {
                var tail = value[multi.Length..];
                return tail.Length == 0 || tail[0] is '(' or '[' || string.IsNullOrWhiteSpace(tail);
            }
        }
        // a quoted identifier may contain spaces; nothing else may
        var inQuotes = false;
        var depth = 0;
        foreach (var c in value)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (inQuotes) continue;
            else if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (char.IsWhiteSpace(c) && depth == 0) return false;
        }
        return inQuotes is false;
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
            if (_requireAt && trimmed.StartsWith('@') is false)
            {
                continue; // RequireAtPrefix: text
            }
            var s = trimmed.StartsWith('@') ? trimmed[1..] : trimmed;

            if (TryKeyword(s, "define_param", out var rest) is false)
            {
                continue; // not the keyword (define_params, define_parameter ...), or no name provided
            }

            var parts = rest.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1)
            {
                continue;
            }

            var name = parts[0];
            var type = parts.Length >= 2 ? parts[1] : null;
            result.VirtualParams.Add((name, type));
            result.ConsumedCommentLines.Add(trimmed);
        }
    }

    /// <summary>
    /// Extract parameter type hints from @param annotations.
    /// Positional patterns: @param $1 name type, @param $1 is name type.
    /// Named patterns (when <paramref name="namedParams"/> is supplied — the file uses :name placeholders):
    /// @param :name type, @param name type is type.
    /// Only extracts when an explicit type is present. Returns null if no type hints found.
    /// </summary>
    public static Dictionary<int, string>? ExtractParamTypeHints(string? comment, IReadOnlyList<string>? namedParams = null, bool requireAtPrefix = false)
    {
        if (string.IsNullOrEmpty(comment)) return null;

        Dictionary<int, string>? hints = null;
        var lines = comment.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (requireAtPrefix && trimmed.StartsWith('@') is false)
            {
                continue; // RequireAtPrefix: text
            }
            var s = trimmed.StartsWith('@') ? trimmed[1..] : trimmed;

            if (!s.StartsWith("param ", StringComparison.OrdinalIgnoreCase) &&
                !s.StartsWith("parameter ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = s.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
            {
                continue;
            }

            int paramIndex;
            string? typeName = null;

            if (parts[1][0] == '$')
            {
                if (!int.TryParse(parts[1].AsSpan(1), out paramIndex) || paramIndex < 1)
                {
                    continue;
                }

                // @param $1 name type ... → parts[3] is type candidate
                // @param $1 is name type ... → parts[4] is type candidate
                // @param $1 type is type ... → parts[4] is type candidate (retype without renaming; this form was
                //                              read later by the comment parser, but gave no hint to Describe)
                if (parts.Length >= 5 &&
                    parts[2].Equals("type", StringComparison.OrdinalIgnoreCase) &&
                    parts[3].Equals("is", StringComparison.OrdinalIgnoreCase))
                {
                    typeName = TypeCandidate(parts[4]);
                }
                else if (parts.Length >= 4 && parts[2].Equals("is", StringComparison.OrdinalIgnoreCase))
                {
                    if (parts.Length >= 5)
                    {
                        typeName = TypeCandidate(parts[4]);
                    }
                }
                else if (parts.Length >= 4)
                {
                    typeName = TypeCandidate(parts[3]);
                }
            }
            else if (namedParams is not null)
            {
                // @param :name type ...           → parts[2] is type candidate
                // @param name type is type ...    → parts[4] is type candidate
                string name;
                int typeIdx;
                if (parts[1][0] == ':' && parts[1].Length > 1)
                {
                    name = parts[1][1..];
                    typeIdx = 2;
                }
                else if (parts.Length >= 5 &&
                    parts[2].Equals("type", StringComparison.OrdinalIgnoreCase) &&
                    parts[3].Equals("is", StringComparison.OrdinalIgnoreCase))
                {
                    name = parts[1];
                    typeIdx = 4;
                }
                else
                {
                    continue;
                }

                paramIndex = IndexOfName(namedParams, name) + 1;
                if (paramIndex < 1 || parts.Length <= typeIdx)
                {
                    continue;
                }
                typeName = TypeCandidate(parts[typeIdx]);
            }
            else
            {
                continue;
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

        static string? TypeCandidate(string part)
        {
            var candidate = part.TrimEnd(';').ToLowerInvariant();
            return candidate is "default" or "=" ? null : candidate;
        }
    }

    /// <summary>0-based index of a named parameter (case-insensitive), or -1.</summary>
    public static int IndexOfName(IReadOnlyList<string> namedParams, string name)
    {
        for (int i = 0; i < namedParams.Count; i++)
        {
            if (string.Equals(namedParams[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Rewrite named (:name) placeholders in the parsed statements to native positional ($N) placeholders.
    /// Names are assigned ordinals in first-appearance order across the whole file (case-insensitive), so
    /// the same name used repeatedly — including across statements — maps to the SAME parameter. The
    /// rewritten SQL is what gets described and executed; PostgreSQL never sees the :name form.
    ///
    /// Token-aware: strings ('', ""), comments (-- and nested /* */), and dollar-quoted bodies are left
    /// untouched. `::` casts, `:=` assignments, and numeric slice bounds (a[1:3]) never match because a
    /// placeholder requires an identifier-start character immediately after the colon (a slice with a
    /// variable bound, a[1:n], must be written with a space: a[1 : n]).
    ///
    /// Also detects native $N placeholders so the caller can reject files mixing both styles.
    /// </summary>
    public static NamedParamRewriteResult RewriteNamedParameters(List<string> statements)
    {
        var result = new NamedParamRewriteResult();

        for (int s = 0; s < statements.Count; s++)
        {
            var sql = statements[s];
            StringBuilder? sb = null; // created lazily on the first replacement
            var state = State.Normal;
            int blockDepth = 0;
            string dollarTag = "";
            char quote = '\0';

            for (int i = 0; i < sql.Length; i++)
            {
                char c = sql[i];
                char next = i + 1 < sql.Length ? sql[i + 1] : '\0';

                switch (state)
                {
                    case State.Normal:
                        if (c == '-' && next == '-')
                        {
                            state = State.LineComment;
                        }
                        else if (c == '/' && next == '*')
                        {
                            state = State.BlockComment;
                            blockDepth = 1;
                            sb?.Append(c); sb?.Append(next); i++;
                            continue;
                        }
                        else if (c == '\'' || c == '"')
                        {
                            state = State.SingleQuote;
                            quote = c;
                        }
                        else if (c == '$')
                        {
                            // Dollar-quote open ($$ / $tag$) vs positional param ($1). A dollar-quote tag
                            // must start with a letter/underscore, so $ followed by a digit is always $N.
                            if (next == '$' || char.IsAsciiLetter(next) || next == '_')
                            {
                                int t = i + 1;
                                while (t < sql.Length && (char.IsAsciiLetterOrDigit(sql[t]) || sql[t] == '_')) t++;
                                if (t < sql.Length && sql[t] == '$')
                                {
                                    dollarTag = sql[i..(t + 1)];
                                    state = State.DollarQuote;
                                    sb?.Append(dollarTag);
                                    i = t;
                                    continue;
                                }
                            }
                            else if (char.IsAsciiDigit(next))
                            {
                                result.HasPositional = true;
                            }
                        }
                        else if (c == ':')
                        {
                            if (next == ':')
                            {
                                // `::` cast — copy both, never a placeholder
                                sb?.Append("::");
                                i++;
                                continue;
                            }
                            if (char.IsAsciiLetter(next) || next == '_')
                            {
                                int e = i + 1;
                                while (e < sql.Length && (char.IsAsciiLetterOrDigit(sql[e]) || sql[e] == '_')) e++;
                                var name = sql[(i + 1)..e];
                                int idx = IndexOfName(result.Names, name);
                                if (idx < 0)
                                {
                                    result.Names.Add(name);
                                    idx = result.Names.Count - 1;
                                }
                                sb ??= new StringBuilder(sql[..i], sql.Length);
                                sb.Append('$').Append(idx + 1);
                                i = e - 1;
                                continue;
                            }
                        }
                        break;

                    case State.LineComment:
                        if (c == '\n') state = State.Normal;
                        break;

                    case State.BlockComment:
                        if (c == '*' && next == '/')
                        {
                            if (--blockDepth == 0) state = State.Normal;
                            sb?.Append(c); sb?.Append(next); i++;
                            continue;
                        }
                        if (c == '/' && next == '*')
                        {
                            blockDepth++;
                            sb?.Append(c); sb?.Append(next); i++;
                            continue;
                        }
                        break;

                    case State.SingleQuote:
                        if (c == quote)
                        {
                            if (next == quote)
                            {
                                // '' / "" escape — stay in the string
                                sb?.Append(c); sb?.Append(next); i++;
                                continue;
                            }
                            state = State.Normal;
                        }
                        break;

                    case State.DollarQuote:
                        if (c == '$' && i + dollarTag.Length <= sql.Length
                            && sql.AsSpan(i, dollarTag.Length).SequenceEqual(dollarTag))
                        {
                            sb?.Append(dollarTag);
                            i += dollarTag.Length - 1;
                            state = State.Normal;
                            continue;
                        }
                        break;
                }

                sb?.Append(c);
            }

            if (sb is not null)
            {
                statements[s] = sb.ToString();
            }
        }

        return result;
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
