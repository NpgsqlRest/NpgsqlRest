using System.Data;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;

namespace NpgsqlRest.SqlFileSource;

public class DescribeResult
{
    public ColumnInfo[]? Columns { get; set; }
    public string[]? ParameterTypes { get; set; }
    public string? Error { get; set; }
    public bool HasError => Error is not null;
}

public class ColumnInfo
{
    public required string Name { get; set; }
    public required string DataTypeName { get; set; }
    public uint TypeOid { get; set; }
}

/// <summary>
/// Uses the PostgreSQL wire protocol (Parse → Describe → Sync via SchemaOnly)
/// to introspect a SQL statement's parameters and return columns.
/// </summary>
public static partial class SqlFileDescriber
{
    // Match $1, $2, ... $N (positional parameters)
    [GeneratedRegex(@"\$(\d+)", RegexOptions.Compiled)]
    private static partial Regex PositionalParamRegex();

    /// <summary>
    /// Find the maximum $N parameter index in a SQL statement.
    /// Returns 0 if no parameters found.
    /// </summary>
    public static int FindMaxParamIndex(string sql)
    {
        int max = 0;
        foreach (Match match in PositionalParamRegex().Matches(sql))
        {
            if (int.TryParse(match.Groups[1].Value, out int index) && index > max)
            {
                max = index;
            }
        }
        return max;
    }
    
    public static DescribeResult Describe(NpgsqlConnection connection, string sql, int paramCount)
    {
        var result = new DescribeResult();

        using var cmd = new NpgsqlCommand(sql, connection);

        // Add placeholder parameters for each $N
        for (int i = 0; i < paramCount; i++)
        {
            cmd.Parameters.Add(new NpgsqlParameter
            {
                NpgsqlDbType = NpgsqlDbType.Unknown
            });
        }

        cmd.LogCommand(nameof(SqlFileDescriber));

        try
        {
            // Use SchemaOnly without KeyInfo — avoids .NET type mapping overhead.
            // Use reader.GetName/GetDataTypeName instead of GetColumnSchema to avoid
            // failures on custom composite types (GetColumnSchema tries to resolve .NET types).
            using (var reader = cmd.ExecuteReader(CommandBehavior.SchemaOnly))
            {
                result.Columns = new ColumnInfo[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var dataTypeName = reader.GetDataTypeName(i);
                    uint typeOid = 0;

                    try
                    {
                        typeOid = reader.GetDataTypeOID(i);
                    }
                    catch
                    {
                        // OID not available
                    }

                    result.Columns[i] = new ColumnInfo
                    {
                        Name = reader.GetName(i),
                        DataTypeName = dataTypeName,
                        TypeOid = typeOid
                    };
                }

                // Extract parameter types from the command's parameters after Describe
                result.ParameterTypes = new string[paramCount];
                for (int i = 0; i < paramCount; i++)
                {
                    var p = cmd.Parameters[i];
                    result.ParameterTypes[i] = p.NpgsqlDbType == NpgsqlDbType.Unknown ? "unknown" : MapNpgsqlDbTypeToTypeName(p.NpgsqlDbType);
                }
            } // reader closed

            // Resolve unknown type names via pg_catalog (requires reader to be closed first)
            for (int i = 0; i < result.Columns.Length; i++)
            {
                var col = result.Columns[i];
                if ((col.DataTypeName == "-.-" || string.IsNullOrEmpty(col.DataTypeName)) && col.TypeOid > 0)
                {
                    var resolved = ResolveTypeNameByOid(connection, col.TypeOid);
                    if (resolved is not null)
                    {
                        col.DataTypeName = resolved;
                    }
                }
            }
        }
        catch (Npgsql.PostgresException pgEx)
        {
            result.Error = FormatPostgresError(pgEx, sql);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }
    
    private static string? ResolveTypeNameByOid(NpgsqlConnection connection, uint oid)
    {
        using var cmd = new NpgsqlCommand(
            "SELECT (quote_ident(n.nspname) || '.' || quote_ident(t.typname))::regtype::text FROM pg_catalog.pg_type t JOIN pg_catalog.pg_namespace n ON n.oid = t.typnamespace WHERE t.oid = $1",
            connection);
        cmd.Parameters.AddWithValue(NpgsqlDbType.Oid, oid);
        cmd.LogCommand(nameof(SqlFileDescriber));
        var result = cmd.ExecuteScalar();
        return result as string;
    }

    /// <summary>
    /// Format a PostgresException into a compiler-like multi-line error message.
    /// Includes the error position as a line:column reference and a caret pointing at the error.
    /// </summary>
    private static string FormatPostgresError(Npgsql.PostgresException pgEx, string sql)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("error ");
        sb.Append(pgEx.SqlState);
        sb.Append(": ");
        sb.AppendLine(pgEx.MessageText);

        // If we have a position, show the line/column and a caret
        if (pgEx.Position > 0 && pgEx.Position <= sql.Length)
        {
            int pos = pgEx.Position - 1; // 0-based
            int line = 1;
            int lineStart = 0;
            for (int i = 0; i < pos && i < sql.Length; i++)
            {
                if (sql[i] == '\n')
                {
                    line++;
                    lineStart = i + 1;
                }
            }
            int col = pos - lineStart + 1;

            // Extract the source line
            int lineEnd = sql.IndexOf('\n', pos);
            if (lineEnd < 0) lineEnd = sql.Length;
            var sourceLine = sql[lineStart..lineEnd].TrimEnd('\r');

            sb.Append("  at line ");
            sb.Append(line);
            sb.Append(", column ");
            sb.AppendLine(col.ToString());
            sb.Append("  ");
            sb.AppendLine(sourceLine.ToString());
            sb.Append("  ");
            sb.Append(new string(' ', col - 1));
            sb.AppendLine("^");
        }

        if (pgEx.Detail is not null)
        {
            sb.Append("  detail: ");
            sb.AppendLine(pgEx.Detail);
        }
        if (pgEx.Hint is not null)
        {
            sb.Append("  hint: ");
            sb.AppendLine(pgEx.Hint);
        }

        return sb.ToString().TrimEnd();
    }

    private static string MapNpgsqlDbTypeToTypeName(NpgsqlDbType dbType)
    {
        // Strip Array flag for base type lookup
        var baseType = dbType & ~NpgsqlDbType.Array;
        bool isArray = (dbType & NpgsqlDbType.Array) != 0;

        var typeName = baseType switch
        {
            NpgsqlDbType.Smallint => "smallint",
            NpgsqlDbType.Integer => "integer",
            NpgsqlDbType.Bigint => "bigint",
            NpgsqlDbType.Numeric => "numeric",
            NpgsqlDbType.Real => "real",
            NpgsqlDbType.Double => "double precision",
            NpgsqlDbType.Money => "money",
            NpgsqlDbType.Text => "text",
            NpgsqlDbType.Varchar => "character varying",
            NpgsqlDbType.Char => "character",
            NpgsqlDbType.Name => "name",
            NpgsqlDbType.Boolean => "boolean",
            NpgsqlDbType.Uuid => "uuid",
            NpgsqlDbType.Json => "json",
            NpgsqlDbType.Jsonb => "jsonb",
            NpgsqlDbType.Xml => "xml",
            NpgsqlDbType.Date => "date",
            NpgsqlDbType.Timestamp => "timestamp",
            NpgsqlDbType.TimestampTz => "timestamptz",
            NpgsqlDbType.Time => "time",
            NpgsqlDbType.TimeTz => "timetz",
            NpgsqlDbType.Interval => "interval",
            NpgsqlDbType.Bytea => "bytea",
            NpgsqlDbType.Inet => "inet",
            NpgsqlDbType.Cidr => "cidr",
            NpgsqlDbType.MacAddr => "macaddr",
            NpgsqlDbType.Bit => "bit",
            NpgsqlDbType.Varbit => "bit varying",
            NpgsqlDbType.TsQuery => "tsquery",
            NpgsqlDbType.TsVector => "tsvector",
            NpgsqlDbType.Point => "point",
            NpgsqlDbType.Line => "line",
            NpgsqlDbType.LSeg => "lseg",
            NpgsqlDbType.Box => "box",
            NpgsqlDbType.Path => "path",
            NpgsqlDbType.Polygon => "polygon",
            NpgsqlDbType.Circle => "circle",
            NpgsqlDbType.Oid => "oid",
            _ => "text"
        };

        return isArray ? typeName + "[]" : typeName;
    }
}

