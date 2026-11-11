using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using PingTrace.Core;
using PingTrace.Runtime;

namespace PingTrace.Adapter.ActiveDirectory;

/// <summary>
/// Registers Active Directory diagnostic destinations.
/// </summary>
public static class ActiveDirectoryExtensions
{
    /// <summary>
    /// Registers a destination using an existing directory reader.
    /// </summary>
    /// <typeparam name="TReader">The registered reader implementation.</typeparam>
    /// <param name="builder">The PingTrace registration builder.</param>
    /// <param name="name">The destination's local name.</param>
    /// <param name="configuredIdentity">
    /// A non-secret description of the reader's configured identity.
    /// </param>
    /// <returns>The builder for additional registrations.</returns>
    /// <remarks>
    /// Register the reader and its dependencies separately. Deferred resolution
    /// keeps local diagnostics independent of directory infrastructure.
    ///
    /// Selecting the reader type explicitly allows a sample to use simulation
    /// while keeping the same connector and result mapping.
    /// </remarks>
    public static PingTraceBuilder AddActiveDirectoryDestination<TReader>(
        this PingTraceBuilder builder,
        string name,
        string configuredIdentity)
        where TReader : class, IActiveDirectoryDiagnosticsReader
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredIdentity);

        builder.Services.AddScoped<IPingTraceConnector>(provider =>
            new ActiveDirectoryPingTraceConnector(
                name: name,
                resolveReader: () => provider.GetRequiredService<TReader>(),
                configuredIdentity: configuredIdentity,
                logger: provider.GetRequiredService<ILogger<ActiveDirectoryPingTraceConnector>>()));

        return builder;
    }
}