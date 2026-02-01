using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Npgsql;

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
            a.schemaname as schema,
            b.proname as name,
            case
                when b.prokind = 'f' then 'function'
                when b.prokind = 'p' then 'procedure'
                when b.prokind = 'a' then 'aggregate'
                when b.prokind = 'w' then 'window'
                else b.prokind::text
            end || ' ' || funcid::regprocedure as signature,
            calls,
            total_time as total_time_ms,
            self_time as self_time_ms
        from pg_stat_user_functions a join pg_proc b on a.funcid = b.oid
        where $1 is null or a.schemaname similar to $1
        order by a.schemaname, b.proname
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
            now() - state_change as state_duration,
            now() - backend_start as connection_age,
            now() - xact_start as transaction_duration,
            now() - query_start as query_duration,
            left(query, 100) as query_preview,
            query
        from pg_stat_activity
        where pid != pg_backend_pid()
        order by state_duration desc nulls last
        """;

    /// <summary>
    /// Returns statistics for user-defined functions and procedures.
    /// </summary>
    public static async Task HandleRoutinesStats(HttpContext context, string connectionString, string outputFormat, string? schemaSimilarTo, ILogger? logger)
    {
        await ExecuteQueryAndRespond(context, connectionString, RoutinesQuery, outputFormat, schemaSimilarTo, logger);
    }

    /// <summary>
    /// Returns statistics for user tables including size, tuple counts, and vacuum info.
    /// </summary>
    public static async Task HandleTablesStats(HttpContext context, string connectionString, string outputFormat, string? schemaSimilarTo, ILogger? logger)
    {
        await ExecuteQueryAndRespond(context, connectionString, TablesQuery, outputFormat, schemaSimilarTo, logger);
    }

    /// <summary>
    /// Returns statistics for user indexes including scan counts and definitions.
    /// </summary>
    public static async Task HandleIndexesStats(HttpContext context, string connectionString, string outputFormat, string? schemaSimilarTo, ILogger? logger)
    {
        await ExecuteQueryAndRespond(context, connectionString, IndexesQuery, outputFormat, schemaSimilarTo, logger);
    }

    /// <summary>
    /// Returns current database activity from pg_stat_activity.
    /// </summary>
    public static async Task HandleActivityStats(HttpContext context, string connectionString, string outputFormat, ILogger? logger)
    {
        await ExecuteQueryAndRespond(context, connectionString, ActivityQuery, outputFormat, null, logger);
    }

    private static async Task ExecuteQueryAndRespond(HttpContext context, string connectionString, string query, string outputFormat, string? schemaSimilarTo, ILogger? logger)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(context.RequestAborted);

            await using var command = new NpgsqlCommand(query, connection);
            if (query.Contains("$1"))
            {
                command.Parameters.Add(new NpgsqlParameter { Value = schemaSimilarTo ?? (object)DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
            }
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);

            if (string.Equals(outputFormat, "html", StringComparison.OrdinalIgnoreCase))
            {
                await WriteAsHtml(context, reader);
            }
            else
            {
                await WriteAsJson(context, reader);
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
        sb.AppendLine("<table style=\"font-family: Calibri, Arial, sans-serif; font-size: 11pt;\">");

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
