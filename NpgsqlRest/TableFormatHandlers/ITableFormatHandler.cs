using System.IO.Pipelines;
using Npgsql;

namespace NpgsqlRest.TableFormatHandlers;

public interface ITableFormatHandler
{
    /// <summary>
    /// The content type to set on the HTTP response (e.g., "text/html; charset=utf-8").
    /// </summary>
    string ContentType { get; }

    /// <summary>
    /// Renders the result set to the response writer.
    /// The implementation owns the full reader loop (while reader.ReadAsync).
    /// </summary>
    /// <param name="reader">Data reader positioned before the first row</param>
    /// <param name="routine">Routine metadata (column names, types, etc.)</param>
    /// <param name="endpoint">Endpoint configuration</param>
    /// <param name="writer">PipeWriter for streaming to response body</param>
    /// <param name="context">HTTP context</param>
    /// <param name="bufferRows">Number of rows to buffer before flushing</param>
    /// <param name="customParameters">Resolved custom parameters for the current request (with placeholders expanded)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RenderAsync(
        NpgsqlDataReader reader,
        Routine routine,
        RoutineEndpoint endpoint,
        PipeWriter writer,
        HttpContext context,
        ulong bufferRows,
        Dictionary<string, string>? customParameters,
        CancellationToken cancellationToken);
}
