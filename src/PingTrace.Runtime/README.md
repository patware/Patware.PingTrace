# PingTrace.Runtime

**Default Ping and Trace behavior, with explicit dependency registration.**

`PingTrace.Runtime` provides the default implementation of `IPingTrace` and
the `AddPingTrace` service registration extension.

Use it to respond to lightweight diagnostic requests and trace dependencies
through registered connectors.

The library targets .NET 10. It does not require ASP.NET Core or expose
endpoints by itself.

## Install

```shell
dotnet add package Patware.PingTrace.Runtime --version 0.1.0
```

Targets .NET 10. C# namespaces remain `PingTrace.Runtime`.

## Register PingTrace

For an application without dependencies:

```csharp
using PingTrace.Runtime;

builder.Services.AddPingTrace();
```

This registers `DefaultPingTrace` as a scoped `IPingTrace` service and
`TraceAllNode` as a singleton. The shared node identity allows cycle detection
to recognize the same application across request scopes.

Consumers can receive `IPingTrace` through dependency injection. Its operations
are:

| Operation | Result |
| --- | --- |
| `Ping()` | The string `Pong`. |
| `TraceAsync(cancellationToken)` | One local `TraceResult`. |
| `TraceAsync(destination, cancellationToken)` | A collection containing the local result followed by any selected dependency results. |
| `TraceAllAsync(request, cancellationToken)` | The local result followed by all registered destination results, recursively where supported. |

Ping and local Trace do not invoke connectors.

Registration does not contact dependencies. Connector constructors should also
remain inexpensive because the runtime receives the registered connectors
when its scoped instance is constructed.

## Expose ASP.NET Core endpoints

Reference `PingTrace.AspNetCore` in addition to this package:

```csharp
using PingTrace.AspNetCore;
using PingTrace.Runtime;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddPingTrace();

WebApplication app = builder.Build();

app.MapPingTrace();

app.Run();
```

`AddPingTrace` registers the implementation. `MapPingTrace`, supplied by
`PingTrace.AspNetCore`, exposes it over HTTP.

Authorization policies belong to the hosting application. Trace results include
machine and execution identity information, so configure access appropriately
before exposing them.

## Register dependencies

The optional registration callback receives a `PingTraceBuilder`. Adapter
extensions use that builder to register connectors and their dependencies.

For an HTTP destination, also reference `PingTrace.Adapter.HttpClient`:

```csharp
using PingTrace.Adapter.HttpClient;
using PingTrace.Runtime;

builder.Services.AddPingTrace(pingTrace =>
{
    pingTrace.AddHttpDestination("ApiService", client =>
    {
        client.BaseAddress = new Uri("https://api.example.com/");
    });
});
```

Replace the address with your API's address. The remote application must expose
a compatible local Trace endpoint; the HTTP adapter's default path is
`/pingtrace/Trace`. Recursive tracing additionally requires the remote
`/pingtrace/TraceAll` endpoint, or a configured alternative `traceAllPath`.

The callback executes immediately during registration, before the service
provider is built. It configures services rather than executing diagnostics.

`AddPingTrace` returns `IServiceCollection`. Adapter registration belongs inside
the callback.

### Destination names

`ApiService` is a local destination alias. The remote application reports its
own application name in its trace result.

The default runtime compares destination names using
`StringComparer.OrdinalIgnoreCase`.

Connector names must:

- Contain a non-whitespace value.
- Be unique without regard to case.
- Differ from the local application name without regard to case.

The default runtime validates these rules when it is constructed, normally when
`IPingTrace` is first resolved within a scope. Successful service registration
alone does not establish that every destination name is valid.

## Trace behavior

### Local Trace

`TraceAsync(cancellationToken)` creates and completes one local result without
invoking a dependency.

The default values are:

| Field | Value |
| --- | --- |
| `Name` | `Assembly.GetEntryAssembly()?.GetName().Name` |
| `MachineName` | `Environment.MachineName` |
| `Identity` | `Environment.UserDomainName` and `Environment.UserName`, separated by a backslash |
| `Payload` | Managed entry-assembly version, falling back to `Pong` |

The version is the managed entry assembly's assembly version. It is not the
PingTrace library version, package version, or informational version.

The name identifies the managed host's entry assembly, not the
`PingTrace.Runtime` assembly. Construction fails with `InvalidOperationException`
if the host assembly name cannot be determined.

The identity describes the process environment; it is not the authenticated
HTTP caller.

### Destination Trace

`TraceAsync(destination, cancellationToken)` selects one destination.

If the destination matches the local application name, it returns a collection
containing only the completed local result.

For a registered dependency, it:

1. Creates the local result.
2. Invokes the selected connector.
3. Places the local result before the connector's results.
4. Completes the local result with `{version} --> [{destination}]`, using `Pong` for the version portion if unavailable.
5. Returns the combined collection.

The connector owns communication with the dependency. The runtime owns the
calling application's result and preserves the order of the connector's
returned results.

The built-in HTTP connector requests the remote application's local Trace.
It does not automatically trace that application's dependencies.

### Recursive TraceAll

Call `TraceAllAsync(cancellationToken: cancellationToken)` to begin a traversal.
An optional `TraceAllRequest` can supply a smaller depth or earlier deadline.
Create that request when starting the operation: its deadline is initialized
at construction, not reset when reused.

The runtime starts its local result, then visits connectors sequentially in
case-insensitive ordinal name order. Recursive connectors receive the remaining
depth, effective deadline, and a new ancestor array. Other connectors execute
their existing `TraceAsync` operation as leaves. Sequential execution supports
connectors sharing a scoped DbContext.

The default depth is eight connector hops. Validation accepts at most sixteen
hops including the ancestor path. Zero depth succeeds only when the application
has no registered destinations. A repeated node or exhausted depth throws
`InvalidOperationException`; exceptions thrown by connectors propagate and stop
traversal without returning the accumulated results. Diagnostic results returned
by connectors are included, and traversal continues while time remains.

Each invocation uses the earlier of the incoming deadline and thirty seconds
after execution begins. The runtime forwards that effective deadline and links
caller cancellation to it. An expired deadline produces cancellation. Clocks
should be synchronized, and connectors must honor cancellation; execution cannot
be forcibly stopped. There is no separate total request or result-count limit.

Shared dependencies are visited per path. Node identities last for the lifetime
of their service provider, are distinct across application instances, and are
not permanent application IDs or graph relationship fields.

Results are flat and local-first. A leaf application's payload is its version
(or `Pong`); an application with destinations uses `{version} --> [TraceAll]`.
The local result finishes after all destinations complete.

See the [execution guide](https://github.com/patware/Patware.PingTrace/blob/main/docs/architecture/PINGTRACE.md#recursive-traceall)
for transport behavior and full traversal semantics.

### Timing

The local result is completed after the selected connector responds, or after
all destinations complete for TraceAll. Its elapsed time includes dependency
work and local processing. Parent and descendant durations overlap.

This is not a direct measurement of network latency.

`TraceResult` calculates elapsed time from wall-clock timestamps. Clock
adjustments can affect the result.

## Failures and cancellation

Both Trace overloads check cancellation before performing their work.
Destination Trace passes the cancellation token to the selected connector.
TraceAll also checks cancellation and passes a token linked to its deadline
to each connector.

For destination requests:

| Condition | Behavior |
| --- | --- |
| Null destination | Throws `ArgumentNullException`. |
| Empty or whitespace destination | Throws `ArgumentException`. |
| Unknown destination | Throws `KeyNotFoundException`. |
| Connector throws an exception | Propagates the exception to the caller. |
| Connector returns diagnostic results | Includes those results and continues traversal while its deadline permits. |
| Cancellation | Propagates cancellation to the caller. |

If a connector throws an exception, the runtime propagates it without
returning the accumulated results.

A result with `Status = Failed` is returned normally and does not stop later
destinations from being traced while the deadline permits. Each result describes
its own operation; the local application's status is not an aggregate health
verdict for its dependencies.

`AddPingTrace` registers logging services. Built-in connectors log each locally
generated failure at Warning level with its destination, error code, and message,
and attach handled exceptions. Caller cancellation is not logged as a destination
failure. The host owns logging providers, filtering, and retention; logging
registration alone does not store logs.

The runtime does not translate exceptions into HTTP status codes. That
responsibility belongs to the hosting integration.

## Extend the runtime

### Custom connectors

Implement `IPingTraceConnector` and register it with dependency injection.
The registration callback exposes the service collection through
`PingTraceBuilder.Services`.

A connector supplies its local destination name and returns the dependency's
trace results. It must not add the calling application's local result.

Keep connection configuration and protocol-specific behavior in the connector
or its supporting services.

Implement `IRecursivePingTraceConnector` when a dependency can continue TraceAll.
Forward the supplied traversal context rather than starting a fresh traversal.
Register recursive connectors as `IPingTraceConnector` so the runtime discovers
them in its existing connector collection.

### Custom application behavior

Applications can provide their own `IPingTrace` implementation through
dependency injection.

`AddPingTrace` uses `TryAddScoped`, so an existing `IPingTrace` registration is
preserved. Register the custom implementation before calling `AddPingTrace`
when using this behavior.

There is currently no `AddPingTrace<T>()` overload.

Calling `AddPingTrace` repeatedly preserves an existing runtime registration,
but executes each supplied callback. Repeated callbacks can therefore add
duplicate connector registrations.

## Current scope

The runtime supports local tracing, explicitly named destinations, and recursive
TraceAll over registered connectors.

It does not currently provide:

- Dependency discovery.
- Destination-path traversal.
- Permanent application IDs.
- Built-in `IHealthCheck` integration.

`PingTraceOptions` is currently unused. Configuring its `Name` property does
not change the default runtime's application name.

The registration callback is an adapter-registration callback, not an
`IOptions<PingTraceOptions>` configuration API.

## Learn more

- [PingTrace overview](https://github.com/patware/Patware.PingTrace/blob/main/README.md)
- [Core contracts](https://github.com/patware/Patware.PingTrace/blob/main/src/PingTrace.Core/README.md)
- [Execution model](https://github.com/patware/Patware.PingTrace/blob/main/docs/architecture/PINGTRACE.md)
- [Connector architecture](https://github.com/patware/Patware.PingTrace/blob/main/docs/architecture/PLUGIN-ARCHITECTURE.md)
