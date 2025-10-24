﻿using System.Security.Claims;
using System.Text;
using Npgsql;
using NpgsqlRest.Auth;
using static NpgsqlRest.NpgsqlRestOptions;

namespace NpgsqlRest;

public static class Ext
{
    public static void CreateAndOpenSourceConnection(this NpgsqlRestOptions options,
        IServiceProvider? serviceProvider,
        ILogger? logger,
        ref NpgsqlConnection? connection,
        ref bool shouldDispose)
    {
        // Try named connection string first
        if (options.MetadataQueryConnectionName is not null)
        {
            if (options.ConnectionStrings is null)
            {
                throw new ArgumentException("ConnectionStrings must be provided when using named connection strings.");
            }
            if (!options.ConnectionStrings.TryGetValue(options.MetadataQueryConnectionName, out var connectionString))
            {
                throw new ArgumentException($"Connection string '{options.MetadataQueryConnectionName}' not found in ConnectionStrings.");
            }

            if (options.MetadataQuerySchema is not null)
            {
                if (HasSearchPathInConnectionString(connectionString, options.MetadataQuerySchema) is false)
                {
                    var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString)
                    {
                        SearchPath = options.MetadataQuerySchema
                    };
                    connection = new NpgsqlConnection(connectionStringBuilder.ConnectionString);
                }
                else
                {
                    connection = new NpgsqlConnection(connectionString);
                }
                shouldDispose = true;
                logger?.LogDebug("Using named connection string '{name}' with schema '{schema}' for metadata queries.",
                    options.MetadataQueryConnectionName,
                    options.MetadataQuerySchema);
            }
            else
            {
                connection = new NpgsqlConnection(connectionString);
                shouldDispose = true;
                logger?.LogDebug("Using named connection string '{name}' for metadata queries.",
                    options.MetadataQueryConnectionName);
            }
            
            connection.OpenRetry(Options.ConnectionRetryOptions, logger);
            return;
        }

        // Try service provider
        if (options.ServiceProviderMode != ServiceProviderObject.None)
        {
            if (serviceProvider is null)
            {
                throw new ArgumentException($"ServiceProvider must be provided when ServiceProviderMode is set to {options.ServiceProviderMode}.");
            }

            if (options.ServiceProviderMode == ServiceProviderObject.NpgsqlDataSource)
            {
                var dataSource = serviceProvider.GetRequiredService<NpgsqlDataSource>();
                connection = dataSource.OpenConnection();
                shouldDispose = true;

                if (options.MetadataQuerySchema is not null && HasSearchPathInConnectionString(connection.ConnectionString, options.MetadataQuerySchema) is false)
                {
                    SetSearchPath(connection, options.MetadataQuerySchema);
                    logger?.LogDebug("Using NpgsqlDataSource from service provider with schema '{schema}' for metadata queries.",
                        options.MetadataQuerySchema);
                }
                else
                {
                    logger?.LogDebug("Using NpgsqlDataSource from service provider for metadata queries.");
                }
            }
            else if (options.ServiceProviderMode == ServiceProviderObject.NpgsqlConnection)
            {
                shouldDispose = false;
                connection = serviceProvider.GetRequiredService<NpgsqlConnection>();

                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.OpenRetry(Options.ConnectionRetryOptions, logger);
                }

                if (options.MetadataQuerySchema is not null && HasSearchPathInConnectionString(connection.ConnectionString, options.MetadataQuerySchema) is false)
                {
                    SetSearchPath(connection, options.MetadataQuerySchema);
                    logger?.LogDebug("Using NpgsqlConnection from service provider with schema '{schema}' for metadata queries.",
                        options.MetadataQuerySchema);
                }
                else
                {
                    logger?.LogDebug("Using NpgsqlConnection from service provider for metadata queries.");
                }
            }
            return;
        }

        // Try DataSource
        if (options.DataSource is not null)
        {
            connection = options.DataSource.CreateConnection();
            shouldDispose = true;
            connection.OpenRetry(Options.ConnectionRetryOptions, logger);

            if (options.MetadataQuerySchema is not null && HasSearchPathInConnectionString(connection.ConnectionString, options.MetadataQuerySchema) is false)
            {
                SetSearchPath(connection, options.MetadataQuerySchema);
                logger?.LogDebug("Using DataSource with schema '{schema}' for metadata queries.",
                    options.MetadataQuerySchema);
            }
            else
            {
                logger?.LogDebug("Using DataSource for metadata queries.");
            }
            return;
        }

        // Fall back to connection string
        if (string.IsNullOrEmpty(options.ConnectionString))
        {
            throw new ArgumentException("ConnectionString must be provided when no other connection source is configured.");
        }

        if (options.MetadataQuerySchema is not null)
        {
            if (HasSearchPathInConnectionString(options.ConnectionString, options.MetadataQuerySchema) is false)
            {
                var connectionStringBuilder = new NpgsqlConnectionStringBuilder(options.ConnectionString)
                {
                    SearchPath = options.MetadataQuerySchema
                };
                connection = new NpgsqlConnection(connectionStringBuilder.ConnectionString);
            }
            else
            {
                connection = new NpgsqlConnection(options.ConnectionString);
            }
            shouldDispose = true;
            connection.OpenRetry(Options.ConnectionRetryOptions, logger);
            logger?.LogDebug("Using default connection string with schema '{schema}' for metadata queries.",
                options.MetadataQuerySchema);
        }
        else
        {
            connection = new NpgsqlConnection(options.ConnectionString);
            shouldDispose = true;
            connection.OpenRetry(Options.ConnectionRetryOptions, logger);
            logger?.LogDebug("Using default connection string for metadata queries.");
        }
    }

    private static bool HasSearchPathInConnectionString(string connectionString, string schema)
    {
        // Fast string search for "Search Path=schema" pattern
        // This avoids expensive NpgsqlConnectionStringBuilder parsing
        int index = connectionString.IndexOf("Search Path", StringComparison.OrdinalIgnoreCase);
        if (index == -1)
        {
            return false;
        }

        // Find the value after the key
        int equalsIndex = connectionString.IndexOf('=', index);
        if (equalsIndex == -1)
        {
            return false;
        }

        // Skip whitespace after '='
        int valueStart = equalsIndex + 1;
        while (valueStart < connectionString.Length && char.IsWhiteSpace(connectionString[valueStart]))
        {
            valueStart++;
        }

        // Find end of value (semicolon or end of string)
        int valueEnd = connectionString.IndexOf(';', valueStart);
        if (valueEnd == -1)
        {
            valueEnd = connectionString.Length;
        }

        // Extract and trim the value
        int valueLength = valueEnd - valueStart;
        if (valueLength == 0)
        {
            return false;
        }

        var value = connectionString.AsSpan(valueStart, valueLength).Trim();
        return value.Equals(schema.AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    private static void SetSearchPath(NpgsqlConnection connection, string schema)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SET search_path TO \"{schema.Replace("\"", "\"\"")}\"";
        command.ExecuteNonQuery();
    }
    
    public static T Get<T>(this NpgsqlDataReader reader, int ordinal)
    {
        object? value;
        if (typeof(T) == typeof(short?[]))
        {
            value = reader.GetFieldValue<short?[]>(ordinal);
        } 
        else
        {
            value = reader[ordinal];
        }

        if (value == DBNull.Value)
        {
            return default!;
        }

        // strange bug single char representing as string on older pg versions when using functions
        if (typeof(T) == typeof(char) && value.GetType() == typeof(string))
        {
            if (value is null)
            {
                return default!;
            }
            object c = ((string)value)[0];
            return (T)c;
        }
        return (T)value;
    }

    public static T GetEnum<T>(this string? value) where T : struct
    {
        Enum.TryParse<T>(value, true, out var result);
        // return the first enum (Other) when no match
        return result;
    }

    public static bool IsTypeOf(this Claim claim, string type)
    {
        return string.Equals(claim.Type, type, StringComparison.OrdinalIgnoreCase);
    }

    public static object GetClaimDbParam(this Dictionary<string, object> dict, string key)
    {
        if (dict.TryGetValue(key, out var value))
        {
           if (value is null)
           {
                 return DBNull.Value;
           }
           return value;
        }
        return DBNull.Value;
    }

    public static object GetClaimDbContextParam(this Dictionary<string, object> dict, string key)
    {
        object value = dict.GetClaimDbParam(key);
        if (value == DBNull.Value || value is string)
        {
            return value;
        }
        var list = value as List<string>;
        StringBuilder sb = new(100);
        sb.Append('{');
        for (int i = 0; i < list?.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }
            sb.Append(PgConverters.SerializeString(list[i]));
        }
        sb.Append('}');
        return sb.ToString();
    }
    
    public static Dictionary<string, object> BuildClaimsDictionary(this ClaimsPrincipal? user, NpgsqlRestAuthenticationOptions options)
    {
        Dictionary<string, object> claimValues = [];
        if (user is null || user.Claims is null)
        {
            return claimValues;
        }
        foreach (var claim in user.Claims)
        {
            if (claimValues.TryGetValue(claim.Type, out var existing))
            {
                if (existing is List<string> list)
                {
                    list.Add(claim.Value);
                }
                else
                {
                    var newList = new List<string>(4) { (string)existing, claim.Value };
                    claimValues[claim.Type] = newList;
                }
            }
            else
            {
                if (claim.IsTypeOf(options.DefaultRoleClaimType))
                {
                    claimValues[claim.Type] = new List<string> { claim.Value };
                }
                else
                {
                    claimValues[claim.Type] = claim.Value;
                }
            }
        }
        return claimValues;
    }

    public static object GetUserClaimsDbParam(this ClaimsPrincipal user, Dictionary<string, object> claimValues)
    {
        if (user is null || claimValues is null || claimValues.Count == 0)
        {
            return "{}";
        }
        int estimatedCapacity = 2 + (claimValues.Count * 10);
        foreach (var entry in claimValues)
        {
            estimatedCapacity += entry.Key.Length * 2;

            if (entry.Value is List<string> list)
            {
                estimatedCapacity += 2;
                foreach (var value in list)
                {
                    estimatedCapacity += value.Length * 2 + 3; 
                }
            }
            else
            {
                estimatedCapacity += ((string)entry.Value).Length * 2 + 2;
            }
        }
        StringBuilder sb = new(estimatedCapacity);
        sb.Append('{');
        int i = 0;
        foreach (var entry in claimValues)
        {
            if (i > 0)
            {
                sb.Append(',');
            }
            sb.Append(PgConverters.SerializeString(entry.Key));
            sb.Append(':');
            if (entry.Value is List<string> values)
            {
                sb.Append('[');
                for (int j = 0; j < values.Count; j++)
                {
                    if (j > 0)
                    {
                        sb.Append(',');
                    }
                    sb.Append(PgConverters.SerializeString(values[j]));
                }
                sb.Append(']');
            }
            else
            {
                sb.Append(PgConverters.SerializeString((string)entry.Value));
            }
            i++;
        }
        sb.Append('}');
        return sb.ToString();
    }

    public static string? GetClientIpAddress(this HttpRequest request)
    {
        // Check X-Forwarded-For header
        var forwardedIp = request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedIp))
        {
            int commaIndex = forwardedIp.IndexOf(',');
            return commaIndex > 0 ? forwardedIp[..commaIndex].Trim() : forwardedIp.Trim();
        }

        // Check other headers with null-coalescing operator
        var ip = request.Headers["X-Real-IP"].FirstOrDefault()
              ?? request.Headers["HTTP_X_FORWARDED_FOR"].FirstOrDefault()
              ?? request.Headers["REMOTE_ADDR"].FirstOrDefault();

        return !string.IsNullOrEmpty(ip) ? ip : request.HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    public static object GetClientIpAddressDbParam(this HttpRequest request)
    {
        return request.GetClientIpAddress() as object ?? DBNull.Value;
    }

    private const string Info = "INFO";
    private const string Notice = "NOTICE";
    private const string Warning = "WARNING";

    public static bool IsInfo(this PostgresNotice notice)
    { 
        return string.Equals(notice.Severity, Info, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNotice(this PostgresNotice notice)
    {
        return string.Equals(notice.Severity, Notice, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsWarning(this PostgresNotice notice)
    {
        return string.Equals(notice.Severity, Warning, StringComparison.OrdinalIgnoreCase);
    }

    public static bool? ParameterEnabled(this Dictionary<string, string>? parameters, string key)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return null;
        }
        if (parameters.TryGetValue(key, out var value))
        {
            // Check for "off" values
            if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "off", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "disabled", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "disable", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "0", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Check for "on" values
            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "on", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "enabled", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "enable", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "1", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return null;
        }
        return null;
    }
    
    public static void TraceCommand(this ILogger? logger, NpgsqlCommand command, string name)
    {
        if (logger?.IsEnabled(LogLevel.Trace) is true && logger is not null)
        {
            StringBuilder sb = new();
            for (int i = 0; i < command.Parameters.Count; i++)
            {
                sb.Append('$');
                sb.Append(i+1);
                sb.Append("=");
                sb.Append(PgConverters.SerializeDatbaseObject(command.Parameters[i].Value));
                sb.Append('\n');
            }

            sb.Append(command.CommandText);
            logger?.LogTrace("{name}:\n{query}", name, sb.ToString());
        }
    }

    public static bool IsSsl(this HttpRequest request)
    {
        if (request.IsHttps)
        {
            return true;
        }
        if (string.Equals(request.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return false;
    }
}