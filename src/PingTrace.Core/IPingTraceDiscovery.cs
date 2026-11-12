#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace PingTrace.Core;

/// <summary>
/// Discovers registered diagnostic destinations reachable through an application.
/// </summary>
/// <remarks>
/// Discovery describes registrations without executing diagnostic probes.
/// Remote participants may still need to be contacted.
/// </remarks>
public interface IPingTraceDiscovery
{
    /// <summary>
    /// Discovers destinations within the supplied traversal limits.
    /// </summary>
    /// <param name="request">
    /// The traversal context, or null to begin discovery.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// Known routes and any issues preventing complete discovery.
    /// </returns>
    Task<DiscoveryResult> DiscoverAsync(DiscoveryRequest? request = null, CancellationToken cancellationToken = default);
}