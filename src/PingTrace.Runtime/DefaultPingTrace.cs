using System.Reflection;

using PingTrace.Core;

namespace PingTrace.Runtime;

/// <summary>
/// Provides local diagnostics and invokes explicitly registered dependencies.
/// </summary>
/// <remarks>
/// Ping and local Trace do not invoke connectors. Destination Trace includes
/// the local result followed by the selected connector's results.
/// </remarks>
public sealed class DefaultPingTrace : IPingTrace
{
    private readonly string _name =
        Assembly.GetEntryAssembly()?.GetName().Name
        ?? throw new InvalidOperationException(
            "The host assembly name could not be determined.");

    private readonly TraceAllNode _node;

    private readonly Dictionary<string, IPingTraceConnector> _connectors;

    /// <summary>
    /// Initializes a default implementation without dependencies.
    /// </summary>
    public DefaultPingTrace() : this(Array.Empty<IPingTraceConnector>())
    {
    }

    /// <summary>
    /// Initializes an implementation with configured dependencies.
    /// </summary>
    /// <param name="connectors">The application's connectors.</param>
    /// <remarks>
    /// Manually constructed runtimes receive their own node identity.
    /// Dependency injection uses the constructor accepting a shared node.
    /// </remarks>
    public DefaultPingTrace(IEnumerable<IPingTraceConnector> connectors)
        : this(connectors, new TraceAllNode())
    {
    }

    /// <summary>
    /// Initializes an implementation with configured dependencies and identity.
    /// </summary>
    /// <param name="connectors">The application's connectors.</param>
    /// <param name="node">The application's shared runtime identity.</param>
    /// <exception cref="ArgumentNullException">
    /// The node, connector collection, or a connector is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A connector name is empty, duplicated, or conflicts with the application.
    /// </exception>
    /// <remarks>
    /// Sharing the node across request scopes allows recursive HTTP requests
    /// to detect a return to the same application instance.
    /// </remarks>
    public DefaultPingTrace(IEnumerable<IPingTraceConnector> connectors, TraceAllNode node)
    {
        ArgumentNullException.ThrowIfNull(connectors);
        ArgumentNullException.ThrowIfNull(node);

        _node = node;
        _connectors = new Dictionary<string, IPingTraceConnector>(StringComparer.OrdinalIgnoreCase);

        foreach (IPingTraceConnector connector in connectors)
        {
            ArgumentNullException.ThrowIfNull(connector);
            ArgumentException.ThrowIfNullOrWhiteSpace(connector.Name);

            if (string.Equals(
                connector.Name, _name, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Destination '{connector.Name}' conflicts with " +
                    "the local application name.",
                    nameof(connectors));
            }

            if (!_connectors.TryAdd(connector.Name, connector))
            {
                throw new ArgumentException(
                    $"Destination '{connector.Name}' is registered more than once.",
                    nameof(connectors));
            }
        }
    }

    /// <summary>
    /// Returns the predictable application response.
    /// </summary>
    /// <returns>The string <c>Pong</c>.</returns>
    public string Ping()
    {
        return "Pong";
    }

    /// <summary>
    /// Returns diagnostic information about this application.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token used to cancel the operation.
    /// </param>
    /// <returns>The completed local trace result.</returns>
    /// <remarks>
    /// The payload contains the managed entry assembly's version, allowing
    /// callers to identify the responding application build. This is the
    /// assembly version rather than the informational or package version.
    /// If unavailable, the payload falls back to Pong.
    /// </remarks>
    public Task<TraceResult> TraceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        TraceResult current = CreateLocalResult();

        var payload = Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? "Pong";

        current.Finish(payload);

        return Task.FromResult(current);
    }

    /// <summary>
    /// Traces this application and the selected destination.
    /// </summary>
    /// <param name="destination">
    /// The local application name or a registered destination name.
    /// </param>
    /// <param name="cancellationToken">
    /// The token used to cancel the operation.
    /// </param>
    /// <returns>
    /// The local result followed by any dependency results.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The destination is empty or consists only of whitespace.
    /// </exception>
    /// <exception cref="KeyNotFoundException">
    /// The destination is not registered and is not the local application.
    /// </exception>
    /// <remarks>
    /// A destination matching the local application name returns only the
    /// local result with its version payload.
    ///
    /// For a connector destination, the local payload contains the entry
    /// assembly version followed by " --> [destination]". The version portion
    /// falls back to Pong when unavailable.
    ///
    /// The local result is completed after the dependency responds so its
    /// elapsed time includes the dependency call. Connector results retain
    /// their own payloads and outcomes. Failed diagnostic results are returned
    /// normally. Exceptions thrown by connectors and cancellation propagate
    /// to the caller.
    /// </remarks>
    public async Task<IReadOnlyList<TraceResult>> TraceAsync(string destination, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        if (string.Equals(destination, _name, StringComparison.OrdinalIgnoreCase))
        {
            TraceResult local = await TraceAsync(cancellationToken);
            return new[] { local };
        }

        if (!_connectors.TryGetValue(destination, out IPingTraceConnector? connector))
        {
            throw new KeyNotFoundException(
                $"Unknown trace destination '{destination}'.");
        }

        TraceResult current = CreateLocalResult();

        IReadOnlyList<TraceResult> downstream = await connector.TraceAsync(cancellationToken);

        List<TraceResult> results = new() { current };
        results.AddRange(downstream);

        var v = Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
                    ?? "Pong";

        current.Finish($"{v} --> [{destination}]");

        return results;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">
    /// The traversal request is invalid.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A cycle is detected or the available depth is exhausted.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Cancellation is requested or the execution deadline expires.
    /// </exception>
    /// <remarks>
    /// A local result starts before any connector executes and finishes after
    /// every destination completes. Sequential execution avoids concurrent
    /// operations against a shared scoped DbContext.
    ///
    /// Each child receives a separate ancestor array. A shared dependency on
    /// another branch is therefore traced again.
    ///
    /// Cancellation is cooperative. Connectors must observe their token;
    /// these limits cannot forcibly terminate an uncooperative connector.
    /// </remarks>
    public async Task<IReadOnlyList<TraceResult>> TraceAllAsync(TraceAllRequest? request = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        request ??= new TraceAllRequest();
        request.Validate();

        // Snapshot caller-owned traversal state before awaiting any work.
        Guid[] ancestors = request.Ancestors.ToArray();
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
            throw new InvalidOperationException(
                $"TraceAll detected a cycle at '{_name}'.");
        }

        if (remainingDepth == 0 && _connectors.Count != 0)
        {
            throw new InvalidOperationException(
                $"TraceAll exhausted its depth at '{_name}'.");
        }

        TraceResult current = CreateLocalResult();
        List<TraceResult> results = new() { current };

        foreach (IPingTraceConnector connector in _connectors.Values.OrderBy(connector => connector.Name, StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();

            IReadOnlyList<TraceResult> downstream;

            if (connector is IRecursivePingTraceConnector recursive)
            {
                Guid[] childAncestors = new Guid[ancestors.Length + 1];
                ancestors.CopyTo(childAncestors, 0);
                childAncestors[^1] = _node.Id;

                TraceAllRequest childRequest = new()
                {
                    RemainingDepth = remainingDepth - 1,
                    DeadlineUtc = deadline,
                    Ancestors = childAncestors
                };

                downstream = await recursive.TraceAllAsync(childRequest, token);
            }
            else
            {
                downstream = await connector.TraceAsync(token);
            }

            token.ThrowIfCancellationRequested();
            results.AddRange(downstream);
        }

        token.ThrowIfCancellationRequested();

        string version =
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? "Pong";

        current.Finish(
            _connectors.Count == 0
                ? version
                : $"{version} --> [TraceAll]");

        return results;
    }

    private TraceResult CreateLocalResult()
    {
        return new TraceResult
        {
            Name = _name,
            MachineName = Environment.MachineName,
            Identity = $"{Environment.UserDomainName}\\{Environment.UserName}"
        };
    }
}