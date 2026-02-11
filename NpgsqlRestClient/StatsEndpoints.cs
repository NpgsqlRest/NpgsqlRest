using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlRest;

namespace NpgsqlRestClient;

/// <summary>
/// Provides endpoint handlers for PostgreSQL statistics endpoints.
/// Each method executes a query against pg_stat_* views and returns results as JSON, TSV, or HTML.
/// </summary>
public static class StatsEndpoints
{
    private const string RoutinesQuery = """
        select
            funcid as oid,
            schemaname as schema,
            b.proname as name,
            case
                when b.prokind = 'f' then 'function'
                when b.prokind = 'p' then 'procedure'
                when b.prokind = 'a' then 'aggregate'
                when b.prokind = 'w' then 'window'
                else b.prokind::text
            end || ' ' || funcid::regprocedure as signature,
            calls,
            ltrim(justify_interval(total_time * interval '1 ms')::text, '@ ') as total_time,
            ltrim(justify_interval(self_time * interval '1 ms')::text, '@ ') as self_time
        from pg_stat_user_functions a join pg_proc b on a.funcid = b.oid
        where $1 is null or schemaname similar to $1
        order by
            a.schemaname,
            b.proname
        """;

    private const string TablesQuery = """
        select
            schemaname as schema,
            relname as name,
            n_live_tup as live_tuples,
            n_dead_tup as dead_tuples,
            round(100.0 * n_dead_tup / nullif(n_live_tup + n_dead_tup, 0), 2) as dead_tuple_percent,
            pg_size_pretty(pg_total_relation_size(relid)) as total_size,
            pg_size_pretty(pg_relation_size(relid)) as table_size,
            pg_size_pretty(pg_indexes_size(relid)) as index_size,
            seq_scan as sequential_scans,
            idx_scan as index_scans,
            round(100.0 * idx_scan / nullif(seq_scan + idx_scan, 0), 2) as index_scan_percent,
            n_tup_ins as inserts_count,
            n_tup_upd as updates_count,
            n_tup_del as deletes_count,
            last_vacuum,
            last_autovacuum,
            last_analyze,
            last_autoanalyze,
            vacuum_count + autovacuum_count as total_vacuums,
            analyze_count + autoanalyze_count as total_analyzes
        from pg_stat_user_tables
        where $1 is null or schemaname similar to $1
        order by schemaname, relname
        """;

    private const string IndexesQuery = """
        select
            schemaname as schema,
            relname as table,
            indexrelname AS index,
            indisunique as is_unique,
            indisprimary as is_primary,
            idx_scan AS scans,
            last_idx_scan as last_scan,
            idx_tup_fetch AS tuples_fetched,
            pg_get_indexdef(indexrelid) as definition
        from pg_stat_user_indexes s
        join pg_index i using (indexrelid)
        where $1 is null or schemaname similar to $1
        order by schemaname, relname
        """;

    private const string ActivityQuery = """
        select
            pid,
            usename as user,
            application_name as app,
            client_addr as client_ip,
            datname as database,
            state,
            wait_event_type,
            wait_event,
            ltrim(justify_interval(now() - state_change)::text, '@ ') as state_duration,
            ltrim(justify_interval(now() - backend_start)::text, '@ ') as connection_age,
            ltrim(justify_interval(now() - xact_start)::text, '@ ') as transaction_duration,
            case 
                when state = 'active' then ltrim(justify_interval(now() - query_start)::text, '@ ')
                when state = 'idle' then 'finished ' || ltrim(justify_interval(now() - state_change)::text, '@ ') || ' ago'
                else state
            end as query_duration,
            left(query, 100) as query_preview,
            query
        from pg_stat_activity
        where pid != pg_backend_pid()
        order by state_duration desc nulls last
        """;

    /// <summary>
    /// Returns statistics for user-defined functions and procedures.
    /// </summary>
    public static async Task HandleRoutinesStats(HttpContext context, string connectionString, string outputFormat, string? schemaSimilarTo, bool requireAuthorization, string[]? authorizedRoles, string? roleClaimType, ILogger? logger)
    {
        if (await CheckAuthorization(context, requireAuthorization, authorizedRoles, roleClaimType) is false)
        {
            return;
        }
        await ExecuteQueryAndRespond(context, connectionString, RoutinesQuery, outputFormat, schemaSimilarTo, logger, "Stats.Routines", setupCommands: ["set local intervalstyle = 'postgres_verbose'"]);
    }

    /// <summary>
    /// Returns statistics for user tables including size, tuple counts, and vacuum info.
    /// </summary>
    public static async Task HandleTablesStats(HttpContext context, string connectionString, string outputFormat, string? schemaSimilarTo, bool requireAuthorization, string[]? authorizedRoles, string? roleClaimType, ILogger? logger)
    {
        if (await CheckAuthorization(context, requireAuthorization, authorizedRoles, roleClaimType) is false)
        {
            return;
        }
        await ExecuteQueryAndRespond(context, connectionString, TablesQuery, outputFormat, schemaSimilarTo, logger, "Stats.Tables");
    }

    /// <summary>
    /// Returns statistics for user indexes including scan counts and definitions.
    /// </summary>
    public static async Task HandleIndexesStats(HttpContext context, string connectionString, string outputFormat, string? schemaSimilarTo, bool requireAuthorization, string[]? authorizedRoles, string? roleClaimType, ILogger? logger)
    {
        if (await CheckAuthorization(context, requireAuthorization, authorizedRoles, roleClaimType) is false)
        {
            return;
        }
        await ExecuteQueryAndRespond(context, connectionString, IndexesQuery, outputFormat, schemaSimilarTo, logger, "Stats.Indexes");
    }

    /// <summary>
    /// Returns current database activity from pg_stat_activity.
    /// </summary>
    public static async Task HandleActivityStats(HttpContext context, string connectionString, string outputFormat, bool requireAuthorization, string[]? authorizedRoles, string? roleClaimType, ILogger? logger)
    {
        if (await CheckAuthorization(context, requireAuthorization, authorizedRoles, roleClaimType) is false)
        {
            return;
        }
        await ExecuteQueryAndRespond(context, connectionString, ActivityQuery, outputFormat, null, logger, "Stats.Activity", setupCommands: ["set local intervalstyle = 'postgres_verbose'"]);
    }

    private static async Task<bool> CheckAuthorization(HttpContext context, bool requireAuthorization, string[]? authorizedRoles, string? roleClaimType)
    {
        if (requireAuthorization is false && authorizedRoles is null)
        {
            return true;
        }

        if (context.User?.Identity?.IsAuthenticated is false)
        {
            await Results.Problem(
                type: null,
                statusCode: (int)HttpStatusCode.Unauthorized,
                title: "Unauthorized",
                detail: null).ExecuteAsync(context);
            return false;
        }

        if (authorizedRoles is not null)
        {
            bool ok = false;
            foreach (var claim in context.User?.Claims ?? [])
            {
                if (string.Equals(claim.Type, roleClaimType, StringComparison.Ordinal))
                {
                    if (authorizedRoles.Contains(claim.Value) is true)
                    {
                        ok = true;
                        break;
                    }
                }
            }
            if (ok is false)
            {
                await Results.Problem(
                    type: null,
                    statusCode: (int)HttpStatusCode.Forbidden,
                    title: "Forbidden",
                    detail: null).ExecuteAsync(context);
                return false;
            }
        }

        return true;
    }

    private static async Task ExecuteQueryAndRespond(
        HttpContext context,
        string connectionString,
        string query, string outputFormat,
        string? schemaSimilarTo,
        ILogger? logger,
        string logName,
        string[]? setupCommands = null)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(context.RequestAborted);

            // If setup commands are provided, wrap in a transaction with rollback
            if (setupCommands is { Length: > 0 })
            {
                await using var beginCmd = new NpgsqlCommand("begin", connection);
                CommandLogger.LogCommand(beginCmd, logger, logName);
                await beginCmd.ExecuteNonQueryAsync(context.RequestAborted);

                foreach (var setupSql in setupCommands)
                {
                    await using var setupCmd = new NpgsqlCommand(setupSql, connection);
                    CommandLogger.LogCommand(setupCmd, logger, logName);
                    await setupCmd.ExecuteNonQueryAsync(context.RequestAborted);
                }
            }

            try
            {
                await using var command = new NpgsqlCommand(query, connection);
                if (query.Contains("$1"))
                {
                    command.Parameters.Add(new NpgsqlParameter { Value = schemaSimilarTo ?? (object)DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
                }
                CommandLogger.LogCommand(command, logger, logName);
                await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);

                var format = context.Request.Query.ContainsKey("format") ? context.Request.Query["format"].ToString() : outputFormat;
                if (string.Equals(format, "html", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteAsHtml(context, reader);
                }
                else
                {
                    await WriteAsJson(context, reader);
                }
            }
            finally
            {
                if (setupCommands is { Length: > 0 })
                {
                    await using var rollbackCmd = new NpgsqlCommand("rollback", connection);
                    CommandLogger.LogCommand(rollbackCmd, logger, logName);
                    await rollbackCmd.ExecuteNonQueryAsync(CancellationToken.None);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Request was cancelled, don't log as error
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error executing stats query");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsync("Error retrieving statistics", context.RequestAborted);
        }
    }

    private static async Task WriteAsJson(HttpContext context, NpgsqlDataReader reader)
    {
        context.Response.ContentType = "application/json";

        var columnNames = new string[reader.FieldCount];
        for (int i = 0; i < reader.FieldCount; i++)
        {
            columnNames[i] = ToCamelCase(reader.GetName(i));
        }

        using var stream = new MemoryStream();
        await using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartArray();

        while (await reader.ReadAsync(context.RequestAborted))
        {
            writer.WriteStartObject();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.GetValue(i);
                writer.WritePropertyName(columnNames[i]);
                WriteJsonValue(writer, value);
            }
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        await writer.FlushAsync(context.RequestAborted);

        stream.Position = 0;
        await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    private static void WriteJsonValue(Utf8JsonWriter writer, object value)
    {
        if (value == DBNull.Value || value is null)
        {
            writer.WriteNullValue();
            return;
        }

        switch (value)
        {
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case decimal d:
                writer.WriteNumberValue(d);
                break;
            case double dbl:
                writer.WriteNumberValue(dbl);
                break;
            case float f:
                writer.WriteNumberValue(f);
                break;
            case DateTime dt:
                writer.WriteStringValue(dt.ToString("o"));
                break;
            case DateTimeOffset dto:
                writer.WriteStringValue(dto.ToString("o"));
                break;
            case TimeSpan ts:
                writer.WriteStringValue(ts.ToString());
                break;
            case System.Net.IPAddress ip:
                writer.WriteStringValue(ip.ToString());
                break;
            case uint ui:
                writer.WriteNumberValue(ui);
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        // Handle snake_case to camelCase conversion
        var parts = name.Split('_');
        if (parts.Length == 1)
        {
            // Simple case: just lowercase first char
            return char.ToLowerInvariant(name[0]) + name[1..];
        }

        // Convert snake_case to camelCase
        var sb = new StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            if (string.IsNullOrEmpty(parts[i]))
                continue;

            if (sb.Length == 0)
            {
                sb.Append(parts[i].ToLowerInvariant());
            }
            else
            {
                sb.Append(char.ToUpperInvariant(parts[i][0]));
                if (parts[i].Length > 1)
                    sb.Append(parts[i][1..].ToLowerInvariant());
            }
        }
        return sb.ToString();
    }

    private static async Task WriteAsHtml(HttpContext context, NpgsqlDataReader reader)
    {
        context.Response.ContentType = "text/html; charset=utf-8";

        var sb = new StringBuilder();
        sb.AppendLine(
            "<style>table{font-family:Calibri,Arial,sans-serif;font-size:11pt;border-collapse:collapse}th,td{border:1px solid #d4d4d4;padding:4px 8px}th{background-color:#f5f5f5;font-weight:600}</style>");
        sb.AppendLine("<table>");

        // Write header row
        sb.Append("<tr>");
        for (int i = 0; i < reader.FieldCount; i++)
        {
            sb.Append("<th>");
            sb.Append(HtmlEncode(reader.GetName(i)));
            sb.Append("</th>");
        }
        sb.AppendLine("</tr>");

        // Write data rows
        while (await reader.ReadAsync(context.RequestAborted))
        {
            sb.Append("<tr>");
            for (int i = 0; i < reader.FieldCount; i++)
            {
                sb.Append("<td>");
                var value = reader.GetValue(i);
                if (value != DBNull.Value)
                {
                    sb.Append(HtmlEncode(ConvertValueToString(value) ?? ""));
                }
                sb.Append("</td>");
            }
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</table>");

        await context.Response.WriteAsync(sb.ToString(), context.RequestAborted);
    }

    private static string HtmlEncode(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    private static string? ConvertValueToString(object value)
    {
        if (value == DBNull.Value || value is null)
            return null;

        return value switch
        {
            TimeSpan ts => ts.ToString(),
            System.Net.IPAddress ip => ip.ToString(),
            DateTime dt => dt.ToString("o"),
            DateTimeOffset dto => dto.ToString("o"),
            _ => value.ToString()
        };
    }
}
