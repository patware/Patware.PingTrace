using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using PingTrace.Core;

using NetHttpClient = System.Net.Http.HttpClient;

namespace PingTrace.Adapter.HttpClient;

internal sealed partial class HttpPingTraceConnector
{
    /// <inheritdoc />
    /// <remarks>
    /// Discovery uses the configured HTTP client but does not execute
    /// local or recursive trace endpoints.
    ///
    /// Communication and response failures produce an incomplete branch.
    /// An unavailable discovery endpoint does not prove that its registered
    /// destinations are absent.
    /// </remarks>
    public async Task<DiscoveryResult> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        using NetHttpClient client = _clientFactory.CreateClient(_httpClientName);

        if (client.BaseAddress is null)
        {
            return DiscoveryFailure(
                errorCode: "Discovery.MissingBaseAddress",
                message: "The discovery HTTP client has no configured BaseAddress.",
                exception: null,
                cancellationToken: cancellationToken);
        }

        // Bound buffered discovery responses independently of route validation.
        client.MaxResponseContentBufferSize = 1024 * 1024;

        try
        {
            using HttpResponseMessage response =
                await client.PostAsJsonAsync(
                    requestUri: _discoverPath,
                    value: request,
                    cancellationToken: cancellationToken);

            response.EnsureSuccessStatusCode();

            DiscoveryResult? result = await response.Content.ReadFromJsonAsync<DiscoveryResult>(cancellationToken: cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (result is null)
            {
                throw new JsonException("The discovery response is null.");
            }

            try
            {
                result.Validate(request.RemainingDepth);
            }
            catch (ArgumentException exception)
            {
                throw new JsonException("The discovery response structure is invalid.", exception);
            }

            return result;
        }
        catch (HttpRequestException exception)
        {
            return DiscoveryFailure(
                errorCode: exception.StatusCode is { } status
                    ? $"Discovery.Http.{(int)status}"
                    : $"Discovery.Transport.{exception.HttpRequestError}",
                message: exception.StatusCode is { } code
                    ? $"The discovery endpoint returned HTTP {(int)code}."
                    : "HTTP communication failed while discovering this branch.",
                exception: exception,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested && exception.InnerException is TimeoutException)
        {
            return DiscoveryFailure(
                errorCode: "Discovery.ClientTimeout",
                message: "The HTTP client timeout expired during discovery.",
                exception: exception,
                cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            return DiscoveryFailure(
                errorCode: "Discovery.InvalidResponse",
                message: "The response could not be interpreted as valid discovery data.",
                exception: exception,
                cancellationToken: cancellationToken);
        }
    }

    private DiscoveryResult DiscoveryFailure(
        string errorCode,
        string message,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogWarning(exception, "Discovery through {Destination} failed with {ErrorCode}: {Message}", Name, errorCode, message);

        return new DiscoveryResult
        {
            IsComplete = false,
            Issues =
            [
                new DiscoveryIssue
                {
                    ErrorCode = errorCode,
                    Message = message
                }
            ]
        };
    }
}