using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using Npgsql;

namespace NpgsqlRest;

/// <summary>
/// Request context sent to PostgreSQL together with the routine.
///
/// The request context (user_context, request_headers context) reaches PostgreSQL as settings that the routine reads
/// with current_setting(). They used to be set by a separate batch before the routine ran: one more round trip for
/// every request, and - made at session level - settings that stay on the pooled connection afterwards.
///
/// For the routines that qualify (see <see cref="CanUse"/>) the settings and the routine go to PostgreSQL as one
/// batch of two statements:
///
///     select set_config($1,$2,true) is null,set_config($3,$4,true) is null     -- the request context
///     select public.my_function($1)                                            -- the routine, exactly as before
///
/// PostgreSQL runs the statements of a batch in one transaction, so settings made with is_local = true are visible to
/// the routine and are gone when the batch ends. One round trip, and nothing is left on the connection - which is
/// also why such requests need no connection reset (see NpgsqlRestOptions.ConnectionResetRequired).
///
/// (The "is null" only keeps PostgreSQL from sending every value back.)
/// </summary>
internal static class RequestContextBatch
{
    /// <summary>The result sets that come before the routine's in such a batch: the context statement's.</summary>
    public const int LeadingResultSets = 1;

    // Rendering reads every value in PostgreSQL's text format. A command asks for that with AllResultTypesAreUnknown.
    // A batch has the same switch, and sends the same request for text results, but Npgsql does not expose the batch's
    // switch in its public API: it is still internal in Npgsql 11.0.0, and making it public is on Npgsql's backlog
    // (https://github.com/npgsql/npgsql/issues/3922). This reaches it without reflection, so it also works in trimmed
    // and Native AOT builds. Whether it is there is checked once (IsSupported): with an Npgsql that does not have it,
    // the request context is set the way it always was, by a separate batch. Once Npgsql has a public property, use
    // that and remove the accessor.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_AllResultTypesAreUnknown")]
    private static extern void SetAllResultTypesAreUnknown(NpgsqlBatch batch, bool value);

    /// <summary>True when this Npgsql can return the results of a batch as text (see above).</summary>
    public static bool IsSupported { get; } = Probe();

    private static bool Probe()
    {
        try
        {
            using var batch = new NpgsqlBatch();
            SetAllResultTypesAreUnknown(batch, true);
            return true;
        }
        catch (Exception)
        {
            // MissingMethodException with an Npgsql that has no such member
            return false;
        }
    }

    /// <summary>
    /// True when the request context of this endpoint is sent with its routine. Decided once, when the endpoints
    /// are built. The other endpoints set their context with a separate batch, at session level, as before.
    /// </summary>
    public static bool CanUse(RoutineEndpoint endpoint) => IsSupported && Qualifies(endpoint);

    /// <summary>True when the endpoint sets a request context and nothing about it requires the session-level one.</summary>
    public static bool Qualifies(RoutineEndpoint endpoint)
    {
        if (endpoint.UserContext is false && endpoint.RequestHeadersMode != RequestHeadersMode.Context)
        {
            return false;
        }
        // With WrapInTransaction the settings are already local to the request's transaction. BeforeRoutineCommands
        // and the command callback are the application's own SQL and code: they run on the connection before the
        // routine and may read the context, or work with the routine's command.
        if (Options.WrapInTransaction || Options.BeforeRoutineCommands.Length > 0 || Options.CommandCallbackAsync is not null)
        {
            return false;
        }
        // These run more than the routine on the connection (the upload handler's commands, the login and logout
        // handlers), and all of it has to see the context.
        if (endpoint.Upload || endpoint.Login || endpoint.Logout)
        {
            return false;
        }
        var routine = endpoint.Routine;
        // The statements of a multi-statement SQL file are executed one by one: local settings would not reach the
        // second one.
        if (routine.IsMultiCommand)
        {
            return false;
        }
        // A single column read as binary (bytea): a batch can ask for text results or for binary ones, not for both.
        if (routine.UnknownResultTypeList is not null)
        {
            return false;
        }
        return routine.Type switch
        {
            // SQL that NpgsqlRest generates: a select, insert, update or delete.
            RoutineType.Function or RoutineType.Table or RoutineType.View => true,
            // The user's own statement: when it is a plain query or data change.
            RoutineType.SqlFile => IsQueryOrDataChange(routine.Expression),
            // Procedures can commit, which ends the transaction - and with it the local settings - while the
            // procedure is still running. They keep the session-level context.
            _ => false
        };
    }

    private static readonly string[] QueryKeywords = ["select", "with", "insert", "update", "delete", "merge", "values", "table"];

    /// <summary>
    /// True when the statement starts with a keyword of a query or a data change. Such a statement cannot end the
    /// transaction it runs in (functions it calls cannot commit), so running it in one transaction with the context
    /// statement changes nothing but the lifetime of the settings. Anything else - call, do, set, vacuum, copy ... -
    /// may commit or may not be allowed to share a transaction, and is left alone.
    /// </summary>
    internal static bool IsQueryOrDataChange(string? sql)
    {
        if (sql is null)
        {
            return false;
        }
        var span = sql.AsSpan();
        // leading whitespace and comments
        while (true)
        {
            span = span.TrimStart();
            if (span.StartsWith("--"))
            {
                var lineEnd = span.IndexOf('\n');
                if (lineEnd < 0)
                {
                    return false;
                }
                span = span[(lineEnd + 1)..];
            }
            else if (span.StartsWith("/*"))
            {
                // block comments nest
                var depth = 1;
                var i = 2;
                while (i < span.Length && depth > 0)
                {
                    if (span[i] == '/' && i + 1 < span.Length && span[i + 1] == '*')
                    {
                        depth++;
                        i += 2;
                    }
                    else if (span[i] == '*' && i + 1 < span.Length && span[i + 1] == '/')
                    {
                        depth--;
                        i += 2;
                    }
                    else
                    {
                        i++;
                    }
                }
                if (depth > 0)
                {
                    return false;
                }
                span = span[i..];
            }
            else
            {
                break;
            }
        }
        var length = 0;
        while (length < span.Length && char.IsAsciiLetter(span[length]))
        {
            length++;
        }
        if (length == 0 || (length < span.Length && (char.IsAsciiDigit(span[length]) || span[length] is '_' or '$' or '.')))
        {
            // not a keyword: an identifier such as select_all, or something else entirely
            return false;
        }
        var word = span[..length];
        foreach (var keyword in QueryKeywords)
        {
            if (word.Equals(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The batch that sets <paramref name="context"/> and then runs the routine of <paramref name="routineCommand"/>,
    /// with text results. Takes the command's text and timeout, and its parameters: a parameter belongs to one command
    /// at a time, so they move from the command to the batch.
    /// </summary>
    public static NpgsqlBatch Create(NpgsqlConnection connection, NpgsqlCommand routineCommand, ReadOnlySpan<(string Key, object? Value)> context)
    {
        var batch = new NpgsqlBatch(connection)
        {
            Timeout = routineCommand.CommandTimeout
        };
        SetAllResultTypesAreUnknown(batch, true);

        var contextCommand = new NpgsqlBatchCommand(ContextStatement(context.Length));
        var contextParameters = contextCommand.Parameters;
        foreach (var (key, value) in context)
        {
            contextParameters.Add(NpgsqlRestParameter.CreateTextParam(key));
            contextParameters.Add(NpgsqlRestParameter.CreateTextParam(value));
        }
        batch.BatchCommands.Add(contextCommand);

        var routineBatchCommand = new NpgsqlBatchCommand(routineCommand.CommandText);
        var source = routineCommand.Parameters;
        var count = source.Count;
        if (count > 0)
        {
            var moved = ArrayPool<NpgsqlParameter>.Shared.Rent(count);
            source.CopyTo(moved, 0);
            source.Clear();
            var target = routineBatchCommand.Parameters;
            for (var i = 0; i < count; i++)
            {
                target.Add(moved[i]);
            }
            ArrayPool<NpgsqlParameter>.Shared.Return(moved, clearArray: true);
        }
        batch.BatchCommands.Add(routineBatchCommand);
        return batch;
    }

    private static readonly string?[] ContextStatements = new string?[17];

    /// <summary>The statement that sets <paramref name="count"/> settings from the parameters $1,$2 ... (name, value).</summary>
    internal static string ContextStatement(int count)
    {
        if (count < ContextStatements.Length)
        {
            return ContextStatements[count] ??= BuildContextStatement(count);
        }
        return BuildContextStatement(count);
    }

    private static string BuildContextStatement(int count)
    {
        var sb = new StringBuilder("select ", 8 + count * 36);
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }
            sb.Append("set_config($").Append(i * 2 + 1).Append(",$").Append(i * 2 + 2).Append(",true) is null");
        }
        return sb.ToString();
    }
}
