using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlRest;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace NpgsqlRestClient;

public class PostgresSink(
    string command, 
    LogEventLevel restrictedToMinimumLevel, 
    int paramCount, 
    string? connectionString,
    RetryStrategy? cmdRetryStrategy) : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        if (string.IsNullOrEmpty(connectionString) is true)
        {
            return;
        }
        if (logEvent.Level < restrictedToMinimumLevel)
        {
            return;
        }

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            using var command1 = new NpgsqlCommand(command, connection);

            if (paramCount > 0)
            {
                command1.Parameters.Add(new NpgsqlParameter() { Value = logEvent.Level.ToString() }); // $1
            }
            if (paramCount > 1)
            {
                command1.Parameters.Add(new NpgsqlParameter() { Value = logEvent.RenderMessage() }); // $2
            }
            if (paramCount > 2)
            {
                command1.Parameters.Add(new NpgsqlParameter() { Value = logEvent.Timestamp.UtcDateTime }); // $3
            }
            if (paramCount > 3)
            {
                command1.Parameters.Add(new NpgsqlParameter() { Value = logEvent.Exception?.ToString() ?? (object)DBNull.Value }); // $4
            }
            if (paramCount > 4)
            {
                command1.Parameters.Add(new NpgsqlParameter() { Value = logEvent.Properties["SourceContext"]?.ToString()?.Trim('"') ?? (object)DBNull.Value }); // $5
            }
            connection.Open();
            command1.ExecuteNonQueryWithRetry(cmdRetryStrategy, null);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error writing to Postgres Log Sink:");
            Console.WriteLine(ex);
            Console.ResetColor();
        }
    }
}

public static partial class PostgresSinkSinkExtensions
{
    public static LoggerConfiguration Postgres(
        this LoggerSinkConfiguration loggerConfiguration, 
        string command,
        LogEventLevel restrictedToMinimumLevel,
        string? connectionString,
        RetryStrategy? cmdRetryStrategy)
    {
        var matches = ParameterRegex().Matches(command).ToArray();
        if (matches.Length < 1 || matches.Length > 5)
        {
            throw new ArgumentException("Command should have at least one parameter and maximum five parameters.");
        }
        for(int i = 0; i < matches.Length; i++)
        {
            if (matches[i].Value != $"${i + 1}")
            {
                throw new ArgumentException($"Parameter ${i + 1} is missing in the command.");
            }
        }
        return loggerConfiguration.Sink(new PostgresSink(command, restrictedToMinimumLevel, matches.Length, connectionString, cmdRetryStrategy));
    }

    [GeneratedRegex(@"\$\d+")]
    public static partial Regex ParameterRegex();
}
