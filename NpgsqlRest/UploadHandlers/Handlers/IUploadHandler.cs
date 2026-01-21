using Npgsql;

namespace NpgsqlRest.UploadHandlers.Handlers;

public interface IUploadHandler
{
    /// <summary>
    /// Set the type of the upload handler.
    /// </summary>
    /// <param name="type"></param>
    IUploadHandler SetType(string type)
    {
        return this;
    }

    /// <summary>
    /// Uploads a file from the context.
    /// </summary>
    /// <param name="connection">Opened connection object</param>
    /// <param name="context">Http context</param>
    /// <param name="parameters">Upload parameters, specific for each type</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// JSON string with upload metadata that is passed to the upload metadata parameter.
    /// It can be array of filename, mime type, size, etc. It depends on implementation.
    /// </returns>
    Task<string> UploadAsync(NpgsqlConnection connection, HttpContext context, Dictionary<string, string>? parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Connection in Upload call will be under transaction yes or no.
    /// </summary>
    bool RequiresTransaction { get; }

    /// <summary>
    /// List of parameters that are used in the upload handler.
    /// </summary>
    IEnumerable<string> Parameters => default!;

    /// <summary>
    /// Runs is the subsequent command fails.
    /// </summary>
    /// <param name="connection">Opened connection object</param>
    /// <param name="context"></param>
    /// <param name="exception"></param>
    void OnError(NpgsqlConnection? connection, HttpContext context, Exception? exception);
}
