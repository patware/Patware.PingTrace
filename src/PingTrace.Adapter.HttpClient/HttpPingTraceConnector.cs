using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using PingTrace.Core;

using NetHttpClient = System.Net.Http.HttpClient;

namespace PingTrace.Adapter.HttpClient;

/// <summary>
/// Requests local or recursive diagnostics from remote PingTrace HTTP endpoints.
/// </summary>
/// <remarks>
/// HTTP error responses, transport failures, client timeouts, missing
/// BaseAddress configuration, and invalid diagnostic JSON become failed
/// trace results. Caller cancellation and unexpected errors propagate.
///
/// Handled exceptions are passed to the application's logging pipeline.
/// Public results contain selected diagnostic details rather than raw
/// exception messages.
///
/// Remote diagnostic outcomes are preserved. Successful HTTP transport
/// does not establish that the remote diagnostic operation succeeded.
/// </remarks>
internal sealed partial class HttpPingTraceConnector : IPingTraceConnector, IRecursivePingTraceConnector, IDiscoverablePingTraceConnector, IRoutedPingTraceConnector
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly string _httpClientName;
    private readonly string _tracePath;
    private readonly string _traceAllPath;
    private readonly ILogger<HttpPingTraceConnector> _logger;
    private readonly string _discoverPath;
    private readonly string _traceRoutePath;

    internal HttpPingTraceConnector(
        string name,
        IHttpClientFactory clientFactory,
        string httpClientName,
        string tracePath,
        string traceAllPath,
        ILogger<HttpPingTraceConnector> logger,
        string discoverPath = "/pingtrace/Discover",
        string traceRoutePath = "/pingtrace/TraceRoute"
        )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(httpClientName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tracePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(traceAllPath);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(discoverPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(traceRoutePath);

        Name = name;
        _clientFactory = clientFactory;
        _httpClientName = httpClientName;
        _tracePath = tracePath;
        _traceAllPath = traceAllPath;
        _logger = logger;
        _discoverPath = discoverPath;
        _traceRoutePath = traceRoutePath;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TraceResult>> TraceAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(
            async (client, token) =>
            {
                TraceResult? result = await client.GetFromJsonAsync<TraceResult>(_tracePath, token);

                return result is null || string.IsNullOrWhiteSpace(result.Name)
                    ? throw new JsonException("The response did not contain valid named trace diagnostics.")
                    : [result];
            },
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A failed recursive request produces a diagnostic entry for the
    /// destination. It does not fall back to local Trace or claim that
    /// the destination's dependencies were inspected.
    /// </remarks>
    public async Task<IReadOnlyList<TraceResult>> TraceAllAsync(TraceAllRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        return await ExecuteAsync(
            async (client, token) =>
            {
                using HttpResponseMessage response =
                    await client.PostAsJsonAsync(
                        _traceAllPath,
                        request,
                        token);

                response.EnsureSuccessStatusCode();

                TraceResult[]? results = await response.Content.ReadFromJsonAsync<TraceResult[]>(cancellationToken: token);

                return results is null ||
                    results.Length == 0 ||
                    results.Any(result =>
                        result is null ||
                        string.IsNullOrWhiteSpace(result.Name))
                    ? throw new JsonException("The response did not contain valid named trace diagnostics.")
                    : (IReadOnlyList<TraceResult>)results;
            },
            cancellationToken);
    }

    /// <remarks>
    /// HTTP error responses, transport failures, client timeouts, missing
    /// BaseAddress configuration, and invalid diagnostic JSON become failed
    /// trace results. Caller cancellation and unexpected errors propagate.
    ///
    /// A failed recursive request produces one failure entry. It does not
    /// establish that the destination's dependencies were inspected.
    ///
    /// Successful HTTP transport does not change the outcomes reported by
    /// the remote destination, including an Unknown outcome from older peers.
    ///
    /// The first remote result represents the immediate destination and
    /// receives the name configured by its owner. Descendant names remain
    /// those assigned by their respective owners. This distinguishes
    /// separately registered instances of the same remote application.
    /// </remarks>
    private async Task<IReadOnlyList<TraceResult>> ExecuteAsync(
        Func<NetHttpClient, CancellationToken, Task<IReadOnlyList<TraceResult>>> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        TraceResult failure = new()
        {
            Name = Name
        };

        using NetHttpClient client = _clientFactory.CreateClient(_httpClientName);

        cancellationToken.ThrowIfCancellationRequested();

        if (client.BaseAddress is null)
        {
            failure.Fail(
                TraceFailureKind.Configuration,
                "Http.MissingBaseAddress",
                "The diagnostic HTTP client has no configured BaseAddress.");

            _logger.LogWarning(
                "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                Name,
                failure.ErrorCode,
                failure.Message);

            return [failure];
        }

        // This identifies the configured host, not a verified responding machine.
        failure.MachineName = client.BaseAddress.Host;

        try
        {
            IReadOnlyList<TraceResult> results = await operation(client, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // The first result represents this connector's immediate destination.
            // Descendant names belong to their respective owners.
            results[0].Name = Name;

            return results;
        }
        catch (HttpRequestException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (exception.StatusCode is { } statusCode)
            {
                TraceFailureKind kind = statusCode switch
                {
                    HttpStatusCode.Unauthorized => TraceFailureKind.Authentication,

                    HttpStatusCode.Forbidden => TraceFailureKind.Authorization,

                    _ => TraceFailureKind.RemoteError
                };

                failure.Fail(
                    kind,
                    $"Http.Status.{(int)statusCode}",
                    $"The destination returned HTTP {(int)statusCode} ({statusCode}).");

                _logger.LogWarning(
                    exception,
                    "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                    Name,
                    failure.ErrorCode,
                    failure.Message);
            }
            else
            {
                failure.Fail(
                    TraceFailureKind.Connection,
                    $"Http.Transport.{exception.HttpRequestError}",
                    $"HTTP communication failed ({exception.HttpRequestError}).");

                _logger.LogWarning(
                    exception,
                    "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                    Name,
                    failure.ErrorCode,
                    failure.Message);
            }
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested && exception.InnerException is TimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();

            failure.Fail(
                TraceFailureKind.Timeout,
                "Http.ClientTimeout",
                "The HTTP client timeout expired before diagnostics completed.");

            _logger.LogWarning(
                exception,
                "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                Name,
                failure.ErrorCode,
                failure.Message);
        }
        catch (JsonException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();

            failure.Fail(
                TraceFailureKind.InvalidResponse,
                "Http.InvalidDiagnostics",
                "The response could not be interpreted as the expected trace diagnostics.");

            _logger.LogWarning(
                exception,
                "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                Name,
                failure.ErrorCode,
                failure.Message);
        }

        return [failure];
    }
}