# PingTrace.Core

**The shared contracts for application Ping and Trace diagnostics.**

PingTrace helps applications answer practical questions: Are you responding?
Where are you running? Which dependency did you reach?

`PingTrace.Core` defines the interfaces and result model used by implementations,
adapters, and consumers. It targets .NET 10 and has no package dependencies.

## When to use this package

Reference `PingTrace.Core` when implementing a custom PingTrace service,
building a connector, or consuming trace results without depending on a
particular host or transport.

For the default implementation and dependency registration, use
`PingTrace.Runtime`. For ASP.NET Core endpoints, also use
`PingTrace.AspNetCore`.

HTTP destination support is provided by `PingTrace.Adapter.HttpClient`.

SQL Server diagnostics through a registered DbContext are provided by `PingTrace.Adapter.EntityFramework`.

## Application contract

`IPingTrace` exposes four operations:

| Operation | Return type | Purpose |
| --- | --- | --- |
| `Ping()` | `string` | Returns a lightweight application response. |
| `TraceAsync(cancellationToken)` | `Task<TraceResult>` | Returns the local application's diagnostic result. |
| `TraceAsync(destination, cancellationToken)` | `Task<IReadOnlyList<TraceResult>>` | Returns results for a named destination trace. |
| `TraceAllAsync(request, cancellationToken)` | `Task<IReadOnlyList<TraceResult>>` | Recursively traces registered destinations; the request is optional. |

Keep Ping inexpensive. It should not invoke dependencies or perform business
operations.

Local Trace describes the current application. Destination Trace allows an
implementation to invoke a configured dependency and return the relevant
results.

All asynchronous operations accept a cancellation token. Implementations
should propagate it to downstream operations.

These interfaces do not define HTTP routes, response status codes, authorization
policies, or dependency injection registration.

### Default runtime behavior

The implementation in `PingTrace.Runtime` supplies the following behavior:

- Ping returns `Pong`.
- The local application name comes from the managed host's entry assembly.
- Local Trace does not invoke dependencies.
- A destination matching the local application name returns only the local
  result in a collection.
- A registered dependency returns the local result followed by the connector's
  results.
- Destination names are matched without regard to case.
- An unknown destination throws `KeyNotFoundException`.
- TraceAll visits destinations sequentially in case-insensitive ordinal name
  order and returns the local result followed by each destination's results.
- Failed diagnostic results are returned normally and allow traversal to continue.
- Exceptions thrown by connectors propagate; accumulated partial results are not returned.

These are behaviors of the default runtime, rather than implementations
provided by this package.

## Connector contract

`IPingTraceConnector` represents a configured dependency.

| Member | Purpose |
| --- | --- |
| `Name` | Identifies the destination within the calling application. |
| `TraceAsync(cancellationToken)` | Invokes the dependency and returns its trace results. |

A connector owns the communication needed to reach its dependency, including
the use of its configured client and protocol.

The connector returns the dependency's results. It does not add the calling
application's local result; the runtime owns that responsibility.

The connector name is a local alias. It does not need to match the name
reported by the remote application.

This separation allows different transports to participate in tracing without
introducing transport-specific requirements into the core contracts.

### Recursive connectors and traversal context

`IRecursivePingTraceConnector` extends `IPingTraceConnector` with
`TraceAllAsync(TraceAllRequest, CancellationToken)`. The default runtime invokes
this member during TraceAll. Existing connectors implementing only
`IPingTraceConnector` participate as leaves through their `TraceAsync` method.

`TraceAllRequest` carries `RemainingDepth`, `DeadlineUtc`, and `Ancestors` across
connector boundaries. Defaults are eight connector hops, a deadline thirty
seconds after request construction, and an empty ancestor path. `Validate()`
checks a maximum combined depth and ancestor count of sixteen, nonempty ancestor
identifiers, and a non-default deadline. An expired deadline is valid input;
the runtime reports it as cancellation.

Recursive connectors must forward the supplied context to participating
applications. Starting a new request would discard the caller's cycle history
and execution limits. See the
[execution guide](https://github.com/patware/Patware.PingTrace/blob/main/docs/architecture/PINGTRACE.md#recursive-traceall)
for the default runtime's traversal rules.

Custom `IPingTrace` implementations must implement `TraceAllAsync` as well as
the existing operations.

## Trace results

`TraceResult` describes one application or dependency's contribution to a trace.

| Member | Meaning |
| --- | --- |
| `Name` | Application name or destination alias supplied by the implementation. |
| `MachineName` | Machine or server identification supplied by the implementation. |
| `Identity` | Execution or connection identity supplied by the implementation. |
| `StartedAt` | Start timestamp, initialized to `DateTime.Now` by the constructor. |
| `FinishedAt` | Completion timestamp, assigned by `Finish` or `Fail`. |
| `Payload` | Diagnostic string assigned by `Finish`. |
| `Status` | `Unknown`, `Succeeded`, or `Failed` for this result's operation. |
| `FailureKind` | Classification supported by the observed failure evidence. |
| `ErrorCode` | Stable connector-defined failure identifier. |
| `Message` | Selected human-readable failure explanation. |
| `Elapsed` | Difference between `FinishedAt` and `StartedAt`. |

The constructor initializes the start timestamp. It does not populate the
application name, machine name, identity, or payload.

For example, an implementation can construct a local result as follows:

```csharp
using System;
using PingTrace.Core;

TraceResult result = new TraceResult
{
    Name = "ExampleApplication",
    MachineName = Environment.MachineName,
    Identity = $"{Environment.UserDomainName}\\{Environment.UserName}"
};

result.Finish("Pong");
```

`Finish(string payload)` assigns the payload, marks success, clears earlier
failure details, and captures the completion time. `Fail(failureKind, errorCode,
message)` marks failure and captures completion while preserving existing payload
and identity metadata. Read `Elapsed` after completing the result.

`Unknown` is the default, including when an older HTTP producer omits status.
It must not be interpreted as success. Status describes this entry's operation,
not the aggregate health of descendants. Successful connectivity alone does not
prove that the responding target matches the intended environment.

The model is mutable. Its payload is currently a string, and it has no
application ID or graph relationship fields.

### Interpreting elapsed time

Elapsed time covers the interval between the result's start and completion.
When an implementation finishes its local result after awaiting a dependency,
that interval includes the dependency operation.

It is not a direct measurement of network latency.

The current implementation uses wall-clock timestamps rather than a monotonic
timer. Clock adjustments can affect the calculated duration.

## Scope

The contracts expose explicit recursive TraceAll. They do not expose automatic
dependency discovery or destination-path traversal.

A connector returning multiple results does not itself imply recursive
traversal. Traversal behavior depends on the implementation and connector.

PingTrace complements health checks and deployment diagnostics. These
interfaces do not inherit from `IHealthCheck` or define application readiness
or liveness policies.

## Diagnostic information

Machine names, execution identities, and payloads can contain sensitive
information. Applications exposing trace results should apply authorization
appropriate to their environment and keep secrets out of payloads.

Authorization belongs to the hosting application; this package is
transport-independent.

## Learn more

- [PingTrace overview](https://github.com/patware/Patware.PingTrace/blob/main/README.md)
- [Architecture](https://github.com/patware/Patware.PingTrace/blob/main/docs/architecture/ARCHITECTURE.md)
- [Execution model](https://github.com/patware/Patware.PingTrace/blob/main/docs/architecture/PINGTRACE.md)
- [Connector architecture](https://github.com/patware/Patware.PingTrace/blob/main/docs/architecture/PLUGIN-ARCHITECTURE.md)
