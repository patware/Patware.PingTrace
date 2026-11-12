using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PingTrace.Core;

/// <summary>
/// Executes diagnostics along an explicit sequence of registered connectors.
/// </summary>
public interface IPingTraceRouter
{
    /// <summary>
    /// Traces the supplied route.
    /// </summary>
    /// <param name="request">The route and traversal context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The participating applications and destination results.</returns>
    /// <remarks>
    /// Route execution does not perform discovery or search other branches.
    /// A failed diagnostic result does not cause an alternative route to be tried.
    /// </remarks>
    Task<IReadOnlyList<TraceResult>> TraceRouteAsync(TraceRouteRequest request, CancellationToken cancellationToken = default);
}