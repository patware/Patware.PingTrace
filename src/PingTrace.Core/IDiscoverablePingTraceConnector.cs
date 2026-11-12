using System.Threading;
using System.Threading.Tasks;

namespace PingTrace.Core;

/// <summary>
/// Discovers destinations exposed by a participating dependency.
/// </summary>
/// <remarks>
/// Returned routes are relative to the remote application.
/// The caller prefixes them with this connector's registered name.
/// </remarks>
public interface IDiscoverablePingTraceConnector : IPingTraceConnector
{
    /// <summary>
    /// Requests discovery from the dependency.
    /// </summary>
    /// <param name="request">The remaining traversal context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The dependency's discovery response.</returns>
    Task<DiscoveryResult> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken = default);
}