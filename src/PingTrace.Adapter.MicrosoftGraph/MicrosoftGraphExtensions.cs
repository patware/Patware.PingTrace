using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;

using PingTrace.Core;
using PingTrace.Runtime;

namespace PingTrace.Adapter.MicrosoftGraph;

/// <summary>
/// Registers Microsoft Graph organization destinations for PingTrace.
/// </summary>
public static class MicrosoftGraphExtensions
{
    /// <summary>
    /// Registers an organization probe using an existing Graph client.
    /// </summary>
    /// <param name="builder">The PingTrace registration builder.</param>
    /// <param name="name">The destination's local registration name.</param>
    /// <param name="configuredIdentity">
    /// A non-secret description of the client's fixed application identity.
    /// </param>
    /// <returns>The builder for additional registrations.</returns>
    /// <exception cref="ArgumentNullException">
    /// A required argument is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The name or configured identity is empty or whitespace.
    /// </exception>
    /// <remarks>
    /// Register GraphServiceClient separately using the application's
    /// intended credentials. Resolution is deferred so local PingTrace
    /// operations do not construct or authenticate the client.
    ///
    /// The identity must describe that client's authentication configuration.
    /// Do not include passwords, certificates, secrets, or access tokens.
    ///
    /// This overload assumes a fixed application identity. Delegated and
    /// dynamically selected tenant identities require scoped resolution.
    /// </remarks>
    public static PingTraceBuilder AddGraphOrganizationDestination(
        this PingTraceBuilder builder,
        string name,
        string configuredIdentity)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredIdentity);

        builder.Services.AddScoped<IPingTraceConnector>(provider =>
            new GraphOrganizationPingTraceConnector(
                name: name,
                resolveClient: () => provider.GetRequiredService<GraphServiceClient>(),
                configuredIdentity: configuredIdentity,
                logger: provider.GetRequiredService<ILogger<GraphOrganizationPingTraceConnector>>()));

        return builder;
    }
}