using System.DirectoryServices.Protocols;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using PingTrace.Core;

namespace PingTrace.Adapter.ActiveDirectory;

/// <summary>
/// Maps directory diagnostics into a PingTrace dependency result.
/// </summary>
/// <remarks>
/// Reader resolution is deferred so Ping and local Trace do not construct
/// directory connections. The application owns the reader and its resources.
///
/// Handled exceptions are passed to the application's logging pipeline
/// because the public diagnostic result deliberately contains only
/// selected failure details. The application controls log retention.
/// </remarks>
internal sealed class ActiveDirectoryPingTraceConnector : IPingTraceConnector
{
    private readonly Func<IActiveDirectoryDiagnosticsReader> _resolveReader;
    private readonly string _configuredIdentity;
    private readonly ILogger<ActiveDirectoryPingTraceConnector> _logger;

    internal ActiveDirectoryPingTraceConnector(
        string name,
        Func<IActiveDirectoryDiagnosticsReader> resolveReader, string configuredIdentity,
        ILogger<ActiveDirectoryPingTraceConnector> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(resolveReader);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredIdentity);
        ArgumentNullException.ThrowIfNull(logger);

        Name = name;
        _resolveReader = resolveReader;
        _configuredIdentity = configuredIdentity;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Known directory failures become diagnostic results.
    /// Identity remains configured metadata, not a server-verified identity.
    ///
    /// The built-in reader uses synchronous LDAP operations. Cancellation
    /// is observed between calls and cannot interrupt an executing LDAP call.
    /// Configure a finite connection timeout.
    ///
    /// Unexpected errors from custom readers continue to propagate.
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

        IActiveDirectoryDiagnosticsReader reader = _resolveReader();

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            ActiveDirectoryDiagnostics diagnostics = await reader.ReadDomainAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (diagnostics is null ||
                string.IsNullOrWhiteSpace(diagnostics.DomainController) ||
                string.IsNullOrWhiteSpace(diagnostics.DomainDistinguishedName) ||
                string.IsNullOrWhiteSpace(diagnostics.DomainObjectName))
            {
                result.Fail(
                    TraceFailureKind.InvalidResponse,
                    "Directory.IncompleteDiagnostics",
                    "The directory reader returned incomplete domain diagnostics.");

                _logger.LogWarning(
                    "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                    Name,
                    result.ErrorCode,
                    result.Message);

                return [result];
            }

            result.MachineName = diagnostics.DomainController;

            result.Finish(JsonSerializer.Serialize(new
            {
                diagnostics.DomainDistinguishedName,
                diagnostics.DomainObjectName,
                IdentitySource = "ConfiguredAuthentication",
                Probe = "DomainRead"
            }));
        }
        catch (DirectoryDiagnosticException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();

            result.Fail(
                exception.FailureKind,
                exception.ErrorCode,
                exception.Message);

            _logger.LogWarning(
                exception,
                "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                Name,
                result.ErrorCode,
                result.Message);
        }
        catch (LdapException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();

            (TraceFailureKind kind, string message) = exception.ErrorCode switch
            {
                49 => (TraceFailureKind.Authentication, "LDAP rejected the supplied credentials."),

                50 => (TraceFailureKind.Authorization, "LDAP reported insufficient access rights."),

                85 => (TraceFailureKind.Timeout, "The LDAP client reported a timeout."),

                81 or 91 => (TraceFailureKind.Connection, "The LDAP client could not establish or maintain communication."),

                _ => (TraceFailureKind.Unknown, $"The LDAP client reported error code {exception.ErrorCode}.")
            };

            result.Fail(
                kind,
                $"Ldap.Client.{exception.ErrorCode}",
                message);

            _logger.LogWarning(
                exception,
                "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                Name,
                result.ErrorCode,
                result.Message);
        }
        catch (DirectoryOperationException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (exception.Response is not { } response)
            {
                result.Fail(
                    TraceFailureKind.Unknown,
                    "Ldap.OperationFailure",
                    "The directory operation failed without a response result code.");

                _logger.LogWarning(
                    exception,
                    "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                    Name,
                    result.ErrorCode,
                    result.Message);
            }
            else
            {
                TraceFailureKind kind = response.ResultCode switch
                {
                    ResultCode.TimeLimitExceeded => TraceFailureKind.Timeout,

                    ResultCode.InappropriateAuthentication or
                    ResultCode.AuthMethodNotSupported or
                    ResultCode.StrongAuthRequired => TraceFailureKind.Authentication,

                    ResultCode.InsufficientAccessRights => TraceFailureKind.Authorization,

                    _ => TraceFailureKind.RemoteError
                };

                result.Fail(
                    kind,
                    $"Ldap.Response.{(int)response.ResultCode}",
                    $"LDAP returned {response.ResultCode} " +
                    $"({(int)response.ResultCode}) during the diagnostic operation.");

                _logger.LogWarning(
                    exception,
                    "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                    Name,
                    result.ErrorCode,
                    result.Message);
            }
        }

        return [result];
    }
}