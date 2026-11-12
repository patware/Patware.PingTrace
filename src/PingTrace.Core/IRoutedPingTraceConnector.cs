using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PingTrace.Core;

/// <summary>
/// Forwards an explicit diagnostic route to a participating dependency.
/// </summary>
public interface IRoutedPingTraceConnector : IPingTraceConnector
{
    /// <summary>
    /// Requests execution of the route relative to the remote application.
    /// </summary>
    /// <param name="request">
    /// The remaining route, excluding this connector's consumed alias.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The remote application and downstream diagnostic results.</returns>
    Task<IReadOnlyList<TraceResult>> TraceRouteAsync(TraceRouteRequest request, CancellationToken cancellationToken = default);
}