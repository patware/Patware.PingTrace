using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions;

using PingTrace.Core;

namespace PingTrace.Adapter.MicrosoftGraph;

/// <summary>
/// Reads organization diagnostics using an existing Microsoft Graph client.
/// </summary>
/// <remarks>
/// Reusing the application's client exercises its configured connection.
/// Client resolution is deferred until tracing begins, matching the
/// Entity Framework adapter's behavior.
///
/// A successful organization read does not establish access to Teams
/// resources or other Microsoft Graph operations.
///
/// Handled exceptions are passed to the application's logging pipeline
/// because the public diagnostic result deliberately contains only
/// selected failure details. The application controls log retention.
/// </remarks>
internal sealed class GraphOrganizationPingTraceConnector : IPingTraceConnector
{
    private readonly Func<GraphServiceClient> _resolveClient;
    private readonly string _configuredIdentity;
    private readonly ILogger<GraphOrganizationPingTraceConnector> _logger;

    internal GraphOrganizationPingTraceConnector(
        string name,
        Func<GraphServiceClient> resolveClient,
        string configuredIdentity,
        ILogger<GraphOrganizationPingTraceConnector> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(resolveClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredIdentity);
        ArgumentNullException.ThrowIfNull(logger);

        Name = name;
        _resolveClient = resolveClient;
        _configuredIdentity = configuredIdentity;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    /// <remarks>
    /// MachineName contains the configured API host, because Microsoft
    /// Graph does not expose the physical server handling the request.
    ///
    /// Identity describes the configured authentication identity; it is
    /// not a server-returned assertion of the authenticated principal.
    ///
    /// Timing includes client resolution and the organization request.
    ///
    /// The configured base URL must be absolute and contain a host.
    /// Validation ensures MachineName describes the configured endpoint
    /// and reports invalid configuration before making a request.
    ///
    /// Known API, transport, and response-data failures become diagnostic
    /// results. Cancellation and unexpected errors propagate.
    /// HTTP 401 and 403 are classified separately because authentication
    /// failure and insufficient permission require different corrective actions.
    /// </remarks>
    public async Task<IReadOnlyList<TraceResult>> TraceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        TraceResult result = new()
        {
            Name = Name,
            MachineName = string.Empty,
            Identity = _configuredIdentity
        };

        GraphServiceClient client = _resolveClient();

        cancellationToken.ThrowIfCancellationRequested();

        if (!Uri.TryCreate(client.RequestAdapter.BaseUrl, UriKind.Absolute, out Uri? endpoint) || string.IsNullOrWhiteSpace(endpoint.Host))
        {
            result.Fail(
                TraceFailureKind.Configuration,
                "Graph.InvalidBaseUrl",
                "The Graph client requires an absolute base URL containing a host.");

            _logger.LogWarning(
                "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                Name,
                result.ErrorCode,
                result.Message);

            return [result];
        }

        result.MachineName = endpoint.Host;

        try
        {

            var response = await client.Organization.GetAsync(
                requestConfiguration: request => request.QueryParameters.Select = ["id", "displayName", "verifiedDomains"],
                cancellationToken: cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (response?.Value is not { Count: 1 } organizations)
            {
                result.Fail(
                    TraceFailureKind.InvalidResponse,
                    "Graph.InvalidOrganizationCount",
                    "Microsoft Graph did not return exactly one organization.");

                _logger.LogWarning(
                    "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                    Name,
                    result.ErrorCode,
                    result.Message);

                return [result];

            }

            var organization = organizations[0];

            if (organization is null || string.IsNullOrWhiteSpace(organization.Id))
            {
                result.Fail(
                    TraceFailureKind.InvalidResponse,
                    "Graph.InvalidOrganizationId",
                    "Microsoft Graph returned an organization without an ID.");

                _logger.LogWarning(
                    "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                    Name,
                    result.ErrorCode,
                    result.Message);

                return [result];
            }

            result.Finish(JsonSerializer.Serialize(new
            {
                TenantId = organization.Id,
                organization.DisplayName,
                VerifiedDomains = organization.VerifiedDomains?
                    .Select(domain => domain.Name)
                    .ToArray(),
                IdentitySource = "ConfiguredAuthentication",
                Probe = "OrganizationRead"
            }));

            return [result];
        }
        catch (ApiException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int statusCode = exception.ResponseStatusCode;

            TraceFailureKind kind = statusCode switch
            {
                401 => TraceFailureKind.Authentication,
                403 => TraceFailureKind.Authorization,
                >= 400 and <= 599 => TraceFailureKind.RemoteError,
                _ => TraceFailureKind.Unknown
            };

            result.Fail(
                kind,
                $"Graph.Http.{statusCode}",
                statusCode is >= 400 and <= 599
                    ? $"The Graph organization request returned HTTP {statusCode}."
                    : "The Graph SDK reported an API failure without a recognized HTTP error status.");

            _logger.LogWarning(
                exception,
                "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                Name,
                result.ErrorCode,
                result.Message);
        }
        catch (HttpRequestException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();

            result.Fail(
                TraceFailureKind.Connection,
                $"Graph.Transport.{exception.HttpRequestError}",
                $"Communication during the Graph request failed ({exception.HttpRequestError}).");

            _logger.LogWarning(
                exception,
                "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                Name,
                result.ErrorCode,
                result.Message);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested && exception.InnerException is TimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();

            result.Fail(
                TraceFailureKind.Timeout,
                "Graph.ClientTimeout",
                "The HTTP client timeout expired during the Graph diagnostic request.");

            _logger.LogWarning(
                exception,
                "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                Name,
                result.ErrorCode,
                result.Message);
        }
        catch (JsonException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();

            result.Fail(
                TraceFailureKind.InvalidResponse,
                "Graph.InvalidJson",
                "The Graph response could not be read as the expected JSON data.");

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