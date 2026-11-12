# Extension contracts

PingTrace uses compiled libraries and dependency injection for extensibility.

Applications register the dependencies they want to trace. There is no
dynamic plugin loader or assembly scanning. Discovery enumerates explicitly
registered destinations through optional connector capabilities.

## Choose the extension point

| Requirement | Extension point |
| --- | --- |
| Call another application's HTTP Trace endpoint | `AddHttpDestination` |
| Communicate through another protocol or application client | `IPingTraceConnector` |
| Continue recursive tracing on a participating dependency | `IRecursivePingTraceConnector` |
| Package reusable adapter registration | Extension method on `PingTraceBuilder` |
| Replace application-level Ping and Trace behavior | `IPingTrace` |

For ordinary dependency tracing, prefer adding a connector. The default
runtime already handles the local result, destination selection, and
combining results.

Replace `IPingTrace` when the application needs different orchestration
or local diagnostic behavior.

## Register an HTTP destination

The following complete `Program.cs` example exposes local endpoints and
registers an HTTP dependency.

Reference `PingTrace.Runtime`, `PingTrace.AspNetCore`, and
`PingTrace.Adapter.HttpClient`.

```csharp
using Microsoft.AspNetCore.Builder;

using PingTrace.Adapter.HttpClient;
using PingTrace.AspNetCore;
using PingTrace.Runtime;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPingTrace(pingTrace =>
{
    pingTrace.AddHttpDestination("OrdersApi", client =>
    {
        client.BaseAddress = new Uri("https://orders.example.com/");
        client.Timeout = TimeSpan.FromSeconds(10);
    });
});

var app = builder.Build();

app.MapPingTrace();

app.Run();
```

Replace the example address with the dependency's address. The dependency
must expose a compatible local Trace endpoint.

This registration enables:

```text
GET /pingtrace/Trace?destination=OrdersApi
```

The default runtime invokes the selected connector and returns its own
result followed by the dependency result.

Calling Ping or local Trace does not invoke the dependency.

### Destination names

`OrdersApi` is a local alias. It does not have to match the remote
application's name.

The default runtime matches names without regard to case.

Destination names must be nonempty, unique within the runtime, and different
from the local application name. Validation occurs when the runtime is
constructed.

Several destinations can use the same adapter implementation. Their names
and configuration distinguish them.

### Custom endpoint paths

The default remote path is `/pingtrace/Trace`.

If the remote application maps PingTrace under `/diagnostics`, configure:

```csharp
pingTrace.AddHttpDestination(
    "OrdersApi",
    client =>
    {
        client.BaseAddress = new Uri("https://orders.example.com/");
    },
    tracePath: "/diagnostics/Trace");
```

A leading slash selects a path from the server root.

To preserve a path prefix in the base address, end the base address with
a slash and omit the leading slash from the trace path:

```csharp
pingTrace.AddHttpDestination(
    "OrdersApi",
    client =>
    {
        client.BaseAddress = new Uri("https://example.com/orders/");
    },
    tracePath: "pingtrace/Trace");
```

This requests:

```text
https://example.com/orders/pingtrace/Trace
```

The registration method rejects absolute trace addresses, paths beginning
with `//`, and paths containing backslashes.

### HTTP client configuration

`AddHttpDestination` returns `IHttpClientBuilder`.

Applications can use that builder to configure handlers and other HTTP
client behavior, including authentication.

Each registration currently uses a named client with this name:

```text
PingTrace.Http:<destination name>
```

The connector creates a client when tracing executes and disposes that
client after the operation. The client factory manages the underlying
handler lifetime.

A diagnostic client is separate from an application's business client.
Registering both with the same base address does not automatically copy
client-specific headers, credentials, or handlers.

The host should deliberately align their configuration when the diagnostic
is intended to exercise the same connection path.

The current adapter does not provide an overload that accepts an existing
named-client name.

## Connector contract

A custom connector implements `IPingTraceConnector`.

| Member | Responsibility |
| --- | --- |
| `Name` | Identify the configured destination in the calling application |
| `TraceAsync(CancellationToken)` | Invoke the dependency and return its results |

The connector's responsibilities are:

1. Contact the configured dependency.
2. Use the appropriate protocol and credentials.
3. Pass cancellation to operations that support it.
4. Validate and translate the dependency's response.
5. Return the dependency observations in their intended order.

The connector should not add the calling application's result.
`DefaultPingTrace` creates and prepends that result.

### Result ownership

A connector must distinguish information observed locally from information
reported by the dependency.

For example, using the caller's `Environment.MachineName` does not establish
the database server's machine name.

Likewise, a successful connection does not reveal the remote execution
identity unless the protocol or diagnostic operation supplies it.

When a dependency already returns a `TraceResult`, preserve its identity
and timestamps rather than replacing them with the connector's local
destination alias or execution environment.

The current result payload is a string.

### Construction and lifetime

The default runtime resolves all registered connectors when it is
constructed.

Keep connector constructors inexpensive. Resolve configuration and retain
dependencies there, but perform network operations inside `TraceAsync`.

This matters because connector construction can occur while handling Ping
or local Trace, even though those operations do not invoke connector
tracing methods.

Choose a connector lifetime compatible with its dependencies. The built-in
HTTP connector uses a scoped lifetime.

Do not capture a scoped dependency in a singleton connector.

### Failure and cancellation

The runtime awaits the selected connector and includes failed diagnostic results.
Unexpected exceptions and cancellation propagate.

Connectors report handled operational failures through `TraceResult.Fail(...)`.
A propagated exception stops execution without returning accumulated results.
A connector that returns an empty collection produces a destination trace
containing only the caller's local result; it does not explicitly report
a dependency failure.

Do not silently translate a failed dependency call into an empty successful
collection.

Pass the cancellation token through to outgoing work. Avoid converting
cancellation into a successful diagnostic result.

Use exception types deliberately. The current HTTP destination handler
translates `KeyNotFoundException` into status 404 around the complete
destination operation. A custom connector throwing that exception would
therefore be reported as an unknown destination.

### Multiple results

The connector contract permits several results.

The default runtime appends them after its local result in the order
provided. It does not interpret their names as instructions to invoke
additional connectors.

For direct destination Trace, the built-in HTTP adapter returns one remote result.
For TraceAll, it sends the traversal context to the remote recursive endpoint
and returns that application's complete result array.

### Recursive connector capability

`IRecursivePingTraceConnector` extends `IPingTraceConnector` with
`TraceAllAsync(TraceAllRequest, CancellationToken)`. Register it as an
`IPingTraceConnector` so the runtime includes it in its connector collection.

During TraceAll, the runtime calls this member with the remaining depth,
effective deadline, and ancestor path. Forward that context to the dependency;
starting a fresh request would reset its limits and cycle history.

Connectors implementing only `IPingTraceConnector`, including the EF adapter,
are treated as leaves and receive their existing `TraceAsync` call.
The runtime visits connectors sequentially, preserving scoped DbContext safety.
It adds the local result, handles path-local cycle checks, and propagates failures.
See [recursive execution](PINGTRACE.md#recursive-traceall) for the full contract.

## Register a custom connector

An application-owned connector can be registered using the builder's
service collection.

For an application-provided `OrdersConnector` implementation:

```csharp
builder.Services.AddPingTrace(pingTrace =>
{
    pingTrace.Services.AddScoped<IPingTraceConnector, OrdersConnector>();
});
```

This snippet requires the application's `OrdersConnector` class and its
constructor dependencies to be registered.

The necessary namespaces are:

```csharp
using Microsoft.Extensions.DependencyInjection;

using PingTrace.Core;
using PingTrace.Runtime;
```

Use constructor injection for application clients and other dependencies.

If several destinations use the same connector class with different
configuration, register separate factories or another explicit named
configuration mechanism.

Do not deduplicate destinations solely by connector implementation type:
one application may need several instances of the same adapter type.

## Package an adapter

A reusable adapter typically contains:

- An `IPingTraceConnector` implementation.
- An extension method on `PingTraceBuilder`.
- Adapter-specific configuration and transport dependencies.

The registration extension should validate its inputs, register supporting
services, and register a connector factory where per-destination
configuration is needed.

`PingTraceBuilder.Services` exposes the application's service collection.
Its constructor is internal; consumers obtain the builder through the
`AddPingTrace` callback.

An adapter that extends the builder references Runtime and Core.
Core and Runtime must not gain a dependency on that adapter.

This preserves the existing dependency direction:

```text
Adapter → Runtime → Core
Adapter → Core
```

Transport packages belong in the adapter project. An EF Core adapter,
for example, should not require HTTP-only consumers to reference EF Core.

## Replace application-level behavior

`AddPingTrace()` preserves an existing `IPingTrace` registration.

For an application-provided `MyPingTrace` implementation:

```csharp
builder.Services.AddScoped<IPingTrace, MyPingTrace>();
builder.Services.AddPingTrace();
```

`MyPingTrace` must implement the current Ping, Trace, and TraceAll members.
The ASP.NET Core endpoint adapter will resolve it through `IPingTrace`.

A custom implementation owns its execution behavior. Merely registering
connectors does not cause those connectors to run unless that implementation
uses them.

The default runtime does not wrap a custom implementation or separately
invoke its dependencies.

A generic `AddPingTrace<MyPingTrace>()` registration method has been
discussed but is not currently available.

## Registration callback behavior

The callback passed to `AddPingTrace` runs immediately during registration.

It configures services, not individual requests. Do not retain its builder
as request state.

Repeated parameterless `AddPingTrace()` calls do not add another default
`IPingTrace` registration.

However, each supplied callback executes. Repeating a callback that adds
destinations adds those registrations again and can produce duplicate-name
errors when the runtime is resolved.

## Test an adapter through its public API

Prefer testing the registration extension and resolved runtime together.
This verifies that consumers can actually use the adapter without exposing
internal connector classes.

Useful adapter tests cover:

- Multiple destinations with independent configuration.
- Preservation of the dependency's reported name and payload.
- Correct transport requests.
- Invalid responses and transport failures.
- Cancellation during an executing operation.
- No dependency invocation during Ping or local Trace.
- Correct resource ownership and disposal.

Use an in-memory transport substitute where practical. Separate those
tests from checks requiring a real database, remote service, or deployment.

The HTTP adapter tests use an in-memory message handler. The ASP.NET Core
suite also exercises an adapter against an in-memory PingTrace endpoint.

## Current boundaries

Discovery and explicit routed tracing are optional capabilities in 0.3.0.
Dynamic adapter loading is not provided. TraceAll remains a separate operation.

Adapters are explicitly registered by the application.

Planned adapters and helper APIs are described in
[capability boundaries](../design/CAPABILITY-BOUNDARIES.md).

## Related documentation

- [Architecture](ARCHITECTURE.md)
- [Execution model](PINGTRACE.md)
- [Definitions](PINGTRACE-DEFINITIONS.md)
- [Distributed hosting](DISTRIBUTED-HOSTING.md)
- [Security](SECURITY.md)

## Discovery and routing in 0.3.0

The default runtime implements optional discovery and routed-trace interfaces.
Named Trace retains local/direct precedence and discovers indirect destinations
when needed. Resolution requires complete discovery with one distinct route.
See [discovery and routing](DISCOVERY-AND-ROUTING.md) for contracts, endpoint
responses, owner-defined names, cancellation, limits, and migration behavior.
