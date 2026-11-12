using PingTrace.Core;

namespace PingTrace.Runtime;

public sealed partial class DefaultPingTrace
{
    /// <inheritdoc />
    /// <remarks>
    /// Direct registrations are listed without invoking their diagnostic
    /// operations. Only discovery-capable connectors are contacted.
    ///
    /// Cycles, depth limits, unsupported recursive connectors, and response
    /// limits produce incomplete discovery. Cancellation and unexpected
    /// implementation errors propagate.
    /// </remarks>
    public async Task<DiscoveryResult> DiscoverAsync(DiscoveryRequest? request = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        request ??= new DiscoveryRequest();
        request.Validate();

        int remainingDepth = request.RemainingDepth;
        Guid[] ancestors = [.. request.Ancestors];

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

        DiscoveryResult result = new()
        {
            ApplicationName = _name
        };

        DiscoveryResult Complete()
        {
            token.ThrowIfCancellationRequested();
            result.IsComplete = result.Issues.Count == 0;
            return result;
        }

        void AddIssue(DiscoveryIssue issue)
        {
            if (result.Issues.Count < DiscoveryResult.MaximumIssues)
            {
                result.Issues.Add(issue);
            }
            else
            {
                result.Issues[^1] = new DiscoveryIssue
                {
                    ErrorCode = "Discovery.IssueLimit",
                    Message = "Additional discovery issues were omitted."
                };
            }
        }

        bool AddRoute(DiscoveredRoute route)
        {
            if (result.Routes.Count < DiscoveryResult.MaximumRoutes)
            {
                result.Routes.Add(route);
                return true;
            }

            AddIssue(new DiscoveryIssue
            {
                ErrorCode = "Discovery.RouteLimit",
                Message = "Additional routes were omitted because the response limit was reached."
            });

            return false;
        }

        if (Array.IndexOf(ancestors, _node.Id) >= 0)
        {
            AddIssue(new DiscoveryIssue
            {
                ErrorCode = "Discovery.Cycle",
                Message = "This application already appears on the discovery path."
            });

            return Complete();
        }

        if (remainingDepth == 0)
        {
            if (_connectors.Count != 0)
            {
                AddIssue(new DiscoveryIssue
                {
                    ErrorCode = "Discovery.DepthLimit",
                    Message = "Registered destinations remain beyond the discovery depth limit."
                });
            }

            return Complete();
        }

        IPingTraceConnector[] connectors = [.. _connectors
            .Values
            .OrderBy(connector => connector.Name, StringComparer.OrdinalIgnoreCase)];

        // Preserve immediate registrations before expanding remote branches.
        foreach (IPingTraceConnector connector in connectors)
        {
            token.ThrowIfCancellationRequested();

            if (!AddRoute(new DiscoveredRoute
            {
                Destination = connector.Name,
                Segments = [connector.Name]
            }))
            {
                return Complete();
            }
        }

        foreach (IPingTraceConnector connector in connectors)
        {
            token.ThrowIfCancellationRequested();

            if (connector is not IDiscoverablePingTraceConnector discoverable)
            {
                if (connector is IRecursivePingTraceConnector)
                {
                    AddIssue(new DiscoveryIssue
                    {
                        Segments = [connector.Name],
                        ErrorCode = "Discovery.Unsupported",
                        Message = "This recursive connector does not support discovery."
                    });
                }

                continue;
            }

            Guid[] childAncestors = new Guid[ancestors.Length + 1];
            ancestors.CopyTo(childAncestors, 0);
            childAncestors[^1] = _node.Id;

            DiscoveryRequest childRequest = new()
            {
                RemainingDepth = remainingDepth - 1,
                DeadlineUtc = deadline,
                Ancestors = childAncestors
            };

            DiscoveryResult child = await discoverable.DiscoverAsync(childRequest, token);

            token.ThrowIfCancellationRequested();

            // Validate only the returned data here. Do not disguise exceptions
            // thrown by the connector itself as response-validation failures.
            try
            {
                if (child is null)
                {
                    throw new ArgumentException("The discovery response is null.");
                }

                child.Validate(childRequest.RemainingDepth);
            }
            catch (ArgumentException)
            {
                AddIssue(new DiscoveryIssue
                {
                    Segments = [connector.Name],
                    ErrorCode = "Discovery.InvalidResponse",
                    Message = "The connector returned an invalid discovery response."
                });

                continue;
            }

            foreach (DiscoveredRoute route in child.Routes)
            {
                if (!AddRoute(new DiscoveredRoute
                {
                    Destination = route.Destination,
                    Segments = [connector.Name, .. route.Segments]
                }))
                {
                    return Complete();
                }
            }

            foreach (DiscoveryIssue issue in child.Issues)
            {
                AddIssue(new DiscoveryIssue
                {
                    Segments = [connector.Name, .. issue.Segments],
                    ErrorCode = issue.ErrorCode,
                    Message = issue.Message
                });
            }
        }

        return Complete();
    }
}