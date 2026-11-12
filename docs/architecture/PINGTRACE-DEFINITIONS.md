# PingTrace definitions

This page defines the current implementation. Planned capabilities are
identified separately in [capability boundaries](../design/CAPABILITY-BOUNDARIES.md).

## Ping

Ping returns a predictable application response. The default implementation
returns `Pong` without invoking dependency connectors.

A successful Ping establishes that the responding application endpoint
can answer the request. It does not establish dependency availability
or successful business processing.

## Local Trace

Local Trace describes the responding application without invoking dependencies.

The current .NET method returns `Task<TraceResult>`. The HTTP endpoint
returns one JSON object.

Local Trace provides a baseline for comparison with a destination trace.

## Destination Trace

Destination Trace accepts either the local application name or the name
of a registered connector. Matching is case-insensitive.

When the destination is the local application, the operation returns a
collection containing one local result.

When the destination identifies a connector, the runtime:

1. Creates the local result.
2. Awaits the connector.
3. Appends the connector's results after the local result.
4. Completes the local result.

The current .NET method returns `Task<IReadOnlyList<TraceResult>>`.
The HTTP endpoint returns a JSON array.

Names without a local or direct match trigger discovery. The runtime requires
complete discovery and one distinct route; resolution failures raise
`TraceRoutingException` with a structured error.

## Application name

The default runtime obtains its name from:

```csharp
Assembly.GetEntryAssembly()?.GetName().Name
```

This identifies the managed entry assembly. Test runners and specialized
hosts may supply a different entry assembly than an application author
expects. An unavailable entry assembly causes runtime construction to fail.

The current registration API does not configure the application name.
`PingTraceOptions` exists in the source but is not consumed by registration
or the default runtime.

Diagnostic application IDs have been removed.

## Instance

An instance is a running copy of an application.

Multiple instances may report the same application name. A machine name
provides useful diagnostic context but does not uniquely identify a process.

The current result model has no explicit instance identifier or parent-child
relationship fields.

## Destination name

A destination name is a local alias for a configured dependency.

For example, `WeatherApiClient` can identify the API dependency in the
frontend. Its HTTP result uses `WeatherApiClient`; a local request made directly
to the API uses its entry-assembly name.

Names must be nonempty, unique without regard to case, and different from
the local application name. The default runtime validates these rules
when constructed.

A destination name is not an arbitrary URL supplied by an HTTP caller.

## Connector

`IPingTraceConnector` supplies a destination name and an asynchronous method
returning dependency results.

A connector owns transport-specific communication. The runtime owns the
calling application's result and result ordering.

The runtime preserves the order returned by the connector. A connector
can return several results, but the runtime does not infer a graph or
automatically traverse them.

## HTTP destination

The built-in HTTP adapter uses a named client from `IHttpClientFactory`.

For direct destination Trace, its default remote path is `/pingtrace/Trace`. It expects the remote
endpoint to return a single local trace object with a nonempty name.

The adapter wraps that object in a collection for the runtime.

Named destination Trace does not forward the local destination alias or invoke
the remote application's dependencies. TraceAll instead sends POST to
`/pingtrace/TraceAll` with the traversal context and expects a nonempty array
of named results. Both paths are configurable independently.

The adapter creates its HTTP client when tracing executes. Ping and local
Trace do not send HTTP requests.

## Trace result

| Property | Current behavior |
| --- | --- |
| `Name` | Application name or dependency alias supplied by the implementation |
| `MachineName` | Local machine name or connector-specific server identification |
| `Identity` | Local execution identity or connector-specific identity |
| `Payload` | A string |
| `StartedAt` | `DateTime.Now` captured by the result constructor |
| `FinishedAt` | `DateTime.Now` captured by `Finish(string)` |
| `Elapsed` | `FinishedAt - StartedAt` |

The default local payload is the managed entry assembly's version, falling
back to `Pong` when unavailable. For a connector destination, the default
runtime's local payload is `{version} --> [{destination}]`.

Payload remains a string. Connectors supply their own diagnostic payloads;
the SQL Server connector returns the current database name.

The reported runtime identity does not necessarily identify credentials
used for outgoing authentication.

## Timing

A destination trace's local duration includes the awaited connector call.
It does not measure the entire HTTP request from arrival through final
response serialization.

Parent and child durations overlap and must not simply be added together.

Network transit, authentication, connection establishment, serialization,
remote work, and HTTP resilience policies may affect observed duration.

The current calculation uses wall-clock timestamps. A monotonic duration
measurement is a proposed improvement; it is not implemented.

## Registration builder

`AddPingTrace()` registers the default implementation with a scoped lifetime,
unless an `IPingTrace` implementation is already registered.
It also registers a singleton `TraceAllNode` so recursive requests in different
scopes share the application's runtime identity.

Its optional callback receives a `PingTraceBuilder` during startup.
Adapters use the builder's service collection to register dependencies.

The callback does not execute trace operations or configure
`PingTraceOptions`.

## Endpoint mapping

`MapPingTrace()` maps GET Ping, Trace, TraceAll, and Discover, plus POST TraceAll,
Discover, and TraceRoute, under
`/pingtrace`.

A string argument changes the prefix:

```csharp
app.MapPingTrace("/diagnostics");
```

It returns a route group to which the host can apply endpoint conventions.
Authorization applied to that group affects Ping, Trace, TraceAll, Discover, and TraceRoute endpoints.

Explicit HEAD support, mapping-options objects, and separate authorization
builders are not implemented.

## Trace All

`IPingTrace.TraceAllAsync` recursively visits configured dependencies.
`IRecursivePingTraceConnector` continues traversal with a `TraceAllRequest`;
other connectors participate as leaves through `TraceAsync`.

The default runtime visits connectors sequentially in case-insensitive ordinal
name order. Shared dependencies are visited per path. The request carries the
remaining depth, absolute deadline, and ancestor identities for cycle detection.
Defaults are eight connector hops and a thirty-second cooperative deadline.
Failed results permit continued traversal. Propagated exceptions and cancellation
stop traversal without returning accumulated results.

GET TraceAll starts a traversal; POST TraceAll continues one using JSON context.
Both return flat arrays. See [recursive execution](PINGTRACE.md#recursive-traceall)
for limits and timing semantics.

## Monitoring policy

PingTrace provides responses and diagnostic observations.

The application or monitoring system defines polling schedules, expected
values, thresholds, health classifications, notifications, and recovery
actions.

## Discovery and routing in 0.3.0

The default runtime implements optional discovery and routed-trace interfaces.
Named Trace retains local/direct precedence and discovers indirect destinations
when needed. Resolution requires complete discovery with one distinct route.
See [discovery and routing](DISCOVERY-AND-ROUTING.md) for contracts, endpoint
responses, owner-defined names, cancellation, limits, and migration behavior.
