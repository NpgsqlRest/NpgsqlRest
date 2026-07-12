using System.Text;
using Npgsql;

namespace NpgsqlRest;

/// <summary>
/// Startup verification mode for endpoints that are routed (by the `connection` comment annotation)
/// to a different connection than the one their source discovered them on. Those endpoints rely on
/// the target database having identical routine metadata - this option checks that assumption once
/// at startup instead of letting it surface as a failing request.
/// </summary>
public enum EndpointConnectionVerification
{
    /// <summary>No verification (default).</summary>
    None,
    /// <summary>Log a warning for every routed endpoint whose routine is missing on the target connection.</summary>
    Warn,
    /// <summary>Fail startup when any routed endpoint's routine is missing on the target connection.</summary>
    Fail,
}

/// <summary>
/// Implements <see cref="NpgsqlRestOptions.EndpointConnectionVerification"/>: for each distinct target
/// connection, one batched round-trip checks that every routed function/procedure resolves via
/// to_regprocedure and every routed table/view via to_regclass. Existence/signature only - the result
/// shape is not compared (SqlFile and Other routine types cannot be checked by name and are skipped).
/// </summary>
internal static class ConnectionVerifier
{
    private static NpgsqlRestOptions Options => NpgsqlRestOptions.Options;
    private static ILogger? Logger => NpgsqlRestOptions.Logger;

    internal static void Verify(List<(RoutineEndpoint Endpoint, IEndpointSource Source)>? pairs)
    {
        var mode = Options.EndpointConnectionVerification;
        if (mode == EndpointConnectionVerification.None || pairs is null || pairs.Count == 0)
        {
            return;
        }

        // Group by target connection name: only endpoints routed AWAY from the connection their
        // source discovered them on carry the schema-identity assumption worth verifying.
        Dictionary<string, List<RoutineEndpoint>> byTarget = new(StringComparer.OrdinalIgnoreCase);
        foreach (var (endpoint, source) in pairs)
        {
            var target = endpoint.ConnectionName;
            if (target is null)
            {
                continue;
            }
            var discoveredOn = source.ConnectionName ?? Options.MetadataQueryConnectionName;
            if (string.Equals(target, discoveredOn, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (byTarget.TryGetValue(target, out var list) is false)
            {
                byTarget[target] = list = [];
            }
            list.Add(endpoint);
        }

        List<string>? failures = mode == EndpointConnectionVerification.Fail ? [] : null;

        foreach (var (connectionName, endpoints) in byTarget)
        {
            // signature → endpoints using it, for reporting
            Dictionary<string, List<string>> procSignatures = new(StringComparer.Ordinal);
            Dictionary<string, List<string>> relationNames = new(StringComparer.Ordinal);
            foreach (var endpoint in endpoints)
            {
                var routine = endpoint.Routine;
                Dictionary<string, List<string>> map;
                string key;
                if (routine.Type is RoutineType.Function or RoutineType.Procedure)
                {
                    key = ProcedureSignature(routine);
                    map = procSignatures;
                }
                else if (routine.Type is RoutineType.Table or RoutineType.View)
                {
                    key = string.Concat(QuoteIdent(routine.Schema), ".", QuoteIdent(routine.Name));
                    map = relationNames;
                }
                else
                {
                    continue; // SqlFile / Other - no catalog name to check
                }
                if (map.TryGetValue(key, out var usedBy) is false)
                {
                    map[key] = usedBy = [];
                }
                usedBy.Add(string.Concat(endpoint.Method.ToString(), " ", endpoint.Path));
            }

            if (procSignatures.Count == 0 && relationNames.Count == 0)
            {
                continue;
            }

            NpgsqlConnection? connection = null;
            try
            {
                if (Options.DataSources?.TryGetValue(connectionName, out var dataSource) is true)
                {
                    connection = dataSource.OpenConnection();
                }
                else if (Options.ConnectionStrings?.TryGetValue(connectionName, out var connectionString) is true)
                {
                    connection = new(connectionString);
                    connection.OpenRetry(Options.ConnectionRetryOptions);
                }
                else
                {
                    Report(mode, failures, string.Concat(
                        "Connection '", connectionName, "' used by ", endpoints.Count.ToString(),
                        " endpoint(s) was not found in the DataSources or ConnectionStrings dictionaries."));
                    continue;
                }

                using var command = connection.CreateCommand();
                command.CommandText = """
                    select 0, s from unnest($1::text[]) as s where to_regprocedure(s) is null
                    union all
                    select 1, s from unnest($2::text[]) as s where to_regclass(s) is null
                    """;
                command.AddParameter(procSignatures.Keys.ToArray(), true);
                command.AddParameter(relationNames.Keys.ToArray(), true);
                command.LogCommand(nameof(ConnectionVerifier));
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var isRelation = reader.GetInt32(0) == 1;
                    var missing = reader.GetString(1);
                    var usedBy = isRelation ? relationNames[missing] : procSignatures[missing];
                    Report(mode, failures, string.Concat(
                        isRelation ? "Relation '" : "Routine '", missing,
                        "' was not found on connection '", connectionName,
                        "' - required by endpoint(s): ", string.Join(", ", usedBy), "."));
                }
                Logger?.LogInformation(
                    "Verified {count} endpoint(s) routed to connection '{connectionName}'",
                    endpoints.Count, connectionName);
            }
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException || (ex is PostgresException))
            {
                Report(mode, failures, string.Concat(
                    "Could not verify ", endpoints.Count.ToString(), " endpoint(s) on connection '",
                    connectionName, "': ", ex.Message));
            }
            finally
            {
                connection?.Dispose();
            }
        }

        if (failures is { Count: > 0 })
        {
            var sb = new StringBuilder();
            sb.AppendLine("Routed endpoint verification failed (VerifyRoutedEndpoints is 'Fail'):");
            foreach (var failure in failures)
            {
                sb.Append("  - ").AppendLine(failure);
            }
            throw new InvalidOperationException(sb.ToString());
        }
    }

    private static void Report(EndpointConnectionVerification mode, List<string>? failures, string message)
    {
        if (mode == EndpointConnectionVerification.Fail)
        {
            failures!.Add(message);
        }
        else
        {
            Logger?.LogWarning("{message}", message);
        }
    }

    // "schema"."name"("type1","type2"...) - identifiers quoted so mixed-case/reserved names parse;
    // parameter types come from the discovery metadata (TypeDescriptor.OriginalType).
    private static string ProcedureSignature(Routine routine)
    {
        var sb = new StringBuilder(64);
        sb.Append(QuoteIdent(routine.Schema)).Append('.').Append(QuoteIdent(routine.Name)).Append('(');
        for (var i = 0; i < routine.Parameters.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }
            sb.Append(routine.Parameters[i].TypeDescriptor.OriginalType);
        }
        return sb.Append(')').ToString();
    }

    private static string QuoteIdent(string identifier) =>
        string.Concat("\"", identifier.Replace("\"", "\"\""), "\"");
}
