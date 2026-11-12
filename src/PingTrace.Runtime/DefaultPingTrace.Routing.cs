using System.Reflection;

using PingTrace.Core;

namespace PingTrace.Runtime;

public sealed partial class DefaultPingTrace
{
    /// <inheritdoc />
    /// <remarks>
    /// Only the selected connector path is executed. Sibling destinations
    /// are not probed, and failed diagnostics do not trigger rerouting.
    ///
    /// The deadline is preserved across forwarding calls. Cancellation is
    /// cooperative and cannot forcibly interrupt an uncooperative connector.
    /// </remarks>
    public async Task<IReadOnlyList<TraceResult>> TraceRouteAsync(TraceRouteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        string[] segments = [.. request.Segments];
        Guid[] ancestors = [.. request.Ancestors];
        int remainingDepth = request.RemainingDepth;

        DateTimeOffset localDeadline = DateTimeOffset.UtcNow.AddSeconds(30);
        DateTimeOffset deadline = request.DeadlineUtc < localDeadline
            ? request.DeadlineUtc
            : localDeadline;

        using CancellationTokenSource execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        TimeSpan remainingTime = deadline - DateTimeOffset.UtcNow;

        if (remainingTime <= TimeSpan.Zero)
        {
            execution.Cancel();
        }
        else
        {
            execution.CancelAfter(remainingTime);
        }

        CancellationToken token = execution.Token;
        token.ThrowIfCancellationRequested();

        if (Array.IndexOf(ancestors, _node.Id) >= 0)
        {
            throw RoutingFailure(
                errorCode: "Routing.Cycle",
                message: "The route revisits an application already on its ancestor path.");
        }

        if (segments.Length == 0)
        {
            return [await TraceAsync(token)];
        }

        if (!_connectors.TryGetValue(segments[0], out IPingTraceConnector? connector))
        {
            throw RoutingFailure(
                errorCode: "Routing.SegmentNotRegistered",
                message: $"Application '{_name}' has no destination registered as '{segments[0]}'.");
        }

        IRoutedPingTraceConnector? forwarding = connector as IRoutedPingTraceConnector;

        if (segments.Length > 1 && forwarding is null)
        {
            throw RoutingFailure(
               errorCode: "Routing.ForwardingNotSupported",
               message: $"Destination '{connector.Name}' cannot forward a remaining route.");
        }

        TraceResult current = CreateLocalResult();

        IReadOnlyList<TraceResult> downstream;

        if (segments.Length == 1)
        {
            downstream = await connector.TraceAsync(token);
        }
        else
        {
            downstream = await forwarding!.TraceRouteAsync(
                new TraceRouteRequest
                {
                    Segments = segments[1..],
                    RemainingDepth = remainingDepth - 1,
                    DeadlineUtc = deadline,
                    Ancestors = [.. ancestors, _node.Id]
                },
                token);
        }

        token.ThrowIfCancellationRequested();

        string version =
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? "Pong";

        current.Finish($"{version} --> [{string.Join(" -> ", segments)}]");

        return [current, .. downstream];
    }

    private static TraceRoutingException RoutingFailure(string errorCode, string message)
    {
        return new TraceRoutingException(new TraceRoutingError
        {
            Protocol = TraceRoutingError.ProtocolName,
            ErrorCode = errorCode,
            Message = message
        });
    }
}