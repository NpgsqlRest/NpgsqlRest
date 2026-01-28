using NpgsqlRest;
using NpgsqlRest.Auth;

namespace NpgsqlRestClient.Fido2;

public sealed class PasskeyEndpointContext(
    PasskeyConfig config,
    NpgsqlRestOptions options,
    RetryStrategy? retryStrategy,
    PostgresConnectionNoticeLoggingMode loggingMode,
    ILogger? logger)
{
    public PasskeyConfig Config { get; } = config;
    public NpgsqlRestOptions Options { get; } = options;
    public RetryStrategy? RetryStrategy { get; } = retryStrategy;
    public PostgresConnectionNoticeLoggingMode LoggingMode { get; } = loggingMode;
    public ILogger? Logger { get; } = logger;
}
