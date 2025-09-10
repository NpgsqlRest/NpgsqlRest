using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Npgsql;
using NpgsqlRest;

namespace NpgsqlRestClient;

public class DbDataProtection(
    string? connectionString, 
    string getCommand, 
    string storeCommand, 
    RetryStrategy? cmdRetryStrategy,
    ILogger? logger) 
    : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        var elements = new List<XElement>();

        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var cmd = new NpgsqlCommand(getCommand, connection);
        using var reader = cmd.ExecuteReaderWithRetry(cmdRetryStrategy, logger);
        while (reader.Read())
        {
            elements.Add(XElement.Parse(reader.GetString(0)));
        }

        return elements;
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var cmd = new NpgsqlCommand();
        cmd.Connection = connection;
        cmd.CommandText = storeCommand;
        cmd.Parameters.Add(new NpgsqlParameter() { Value = friendlyName }); // $1
        cmd.Parameters.Add(new NpgsqlParameter() { Value = element.ToString(SaveOptions.DisableFormatting) }); // $2
        cmd.ExecuteNonQueryWithRetry(cmdRetryStrategy, logger);
    }
}
