using Microsoft.Extensions.Logging;

using PingTrace.Core;

using StackExchange.Redis;

namespace PingTrace.Adapater.RedisCache;

/// <summary>
/// Checks Redis connectivity through a registered connection.
/// </summary>
/// <remarks>
/// Connection resolution is deferred until this destination is traced.
/// The connector does not own or dispose the connection.
///
/// Handled exceptions are passed to the application's logging pipeline
/// because the public diagnostic result deliberately contains only
/// selected failure details. The application controls log retention.
/// </remarks>
internal sealed class RedisTraceConnector : IPingTraceConnector
{
    private readonly Func<IConnectionMultiplexer> _getConnection;
    private readonly ILogger<RedisTraceConnector> _logger;

    internal RedisTraceConnector(string name, Func<IConnectionMultiplexer> getConnection, ILogger<RedisTraceConnector> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(getConnection);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        Name = name;
        _getConnection = getConnection;
    }



    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Known Redis failures become structured diagnostic results.
    /// Caller cancellation and unexpected implementation errors propagate.
    /// Connection resolution is deferred until tracing begins.
    ///
    /// The five-second wait limits waiting for the ping task. It does not
    /// bound synchronous connection resolution or cancel the Redis command.
    /// A Redis client timeout may expire independently of that wait.
    ///
    /// Exception messages are not copied into the public result.
    /// </remarks>
    public async Task<IReadOnlyList<TraceResult>> TraceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        TraceResult result = new()
        {
            Name = Name,
            MachineName = string.Empty,
            Identity = string.Empty
        };

        try
        {
            IConnectionMultiplexer connection = _getConnection();

            cancellationToken.ThrowIfCancellationRequested();

            await connection
                .GetDatabase()
                .PingAsync()
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

            result.Finish("PONG");

            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (RedisTimeoutException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();

            result.Fail(
                TraceFailureKind.Timeout,
                "Redis.ClientTimeout",
                "The Redis client reported that its operation timeout expired.");

            _logger.LogWarning(
                exception,
                "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                Name,
                result.ErrorCode,
                result.Message);

        }
        catch (TimeoutException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();

            result.Fail(
                TraceFailureKind.Timeout,
                "Redis.DiagnosticTimeout",
                "The Redis diagnostic operation timed out.");

            _logger.LogWarning(
                exception,
                "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                Name,
                result.ErrorCode,
                result.Message);
        }
        catch (RedisConnectionException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool authenticationFailed = exception.FailureType == ConnectionFailureType.AuthenticationFailure;

            result.Fail(
                authenticationFailed
                    ? TraceFailureKind.Authentication
                    : TraceFailureKind.Connection,
                $"Redis.Connection.{exception.FailureType}",
                authenticationFailed
                    ? "The Redis client reported an authentication failure."
                    : $"Redis communication failed ({exception.FailureType}).");

            _logger.LogWarning(
                exception,
                "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                Name,
                result.ErrorCode,
                result.Message);
        }
        catch (RedisServerException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();

            result.Fail(
                TraceFailureKind.RemoteError,
                "Redis.ServerError",
                "Redis returned an error while executing the diagnostic operation.");

            _logger.LogWarning(
                exception,
                "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                Name,
                result.ErrorCode,
                result.Message);
        }
        catch (RedisException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();

            result.Fail(
                TraceFailureKind.Unknown,
                "Redis.UnclassifiedFailure",
                "The Redis client reported a failure without a more specific classification.");

            _logger.LogWarning(
                exception,
                "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                Name,
                result.ErrorCode,
                result.Message);
        }

        return [result];
    }
}