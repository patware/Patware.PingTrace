using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using PingTrace.Core;

namespace PingTrace.Adapter.HttpClient;

internal sealed partial class HttpPingTraceConnector
{
    /// <inheritdoc />
    /// <remarks>
    /// Recognized routing failures propagate as structured routing errors.
    /// Other HTTP failures use the connector's existing diagnostic-failure
    /// handling. A generic HTTP 404 is not interpreted as a missing
    /// destination registration.
    /// </remarks>
    public async Task<IReadOnlyList<TraceResult>> TraceRouteAsync(TraceRouteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        return await ExecuteAsync(
            async (client, token) =>
            {
                client.MaxResponseContentBufferSize = 1024 * 1024;

                using HttpResponseMessage response =
                    await client.PostAsJsonAsync(
                        requestUri: _traceRoutePath,
                        value: request,
                        cancellationToken: token);

                if (!response.IsSuccessStatusCode)
                {
                    TraceRoutingError? routingError = null;

                    try
                    {
                        routingError = await response.Content.ReadFromJsonAsync<TraceRoutingError>(cancellationToken: token);
                    }
                    catch (JsonException)
                    {
                        // An unrelated server or proxy may return HTML,
                        // plain text, or another JSON error format.
                    }

                    token.ThrowIfCancellationRequested();

                    if (IsRecognizedRouteError(response.StatusCode, routingError))
                    {
                        throw new TraceRoutingException(routingError!);
                    }

                    response.EnsureSuccessStatusCode();
                }

                TraceResult[]? results =
                    await response.Content.ReadFromJsonAsync<TraceResult[]>(
                        cancellationToken: token);

                if (results is null ||
                    results.Length == 0 ||
                    results.Any(result =>
                        result is null ||
                        string.IsNullOrWhiteSpace(result.Name)))
                {
                    throw new JsonException("The routed trace response contains no valid named results.");
                }

                return results;
            },
            cancellationToken);
    }

    private static bool IsRecognizedRouteError(HttpStatusCode statusCode, TraceRoutingError? error)
    {
        if (error is null ||
            error.Protocol != TraceRoutingError.ProtocolName ||
            string.IsNullOrWhiteSpace(error.Message))
        {
            return false;
        }

        // Explicit route execution does not perform name resolution.
        // Therefore only execution-related codes are accepted here.
        return (statusCode, error.ErrorCode) switch
        {
            (HttpStatusCode.NotFound, "Routing.SegmentNotRegistered") => true,

            (HttpStatusCode.Conflict, "Routing.Cycle") => true,

            (HttpStatusCode.NotImplemented, "Routing.ForwardingNotSupported") => true,

            _ => false
        };
    }
}