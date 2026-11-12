# PingTrace.Adapter.HttpClient

**Trace HTTP dependencies through explicitly configured clients.**

`PingTrace.Adapter.HttpClient` connects the PingTrace runtime to remote HTTP
Trace endpoints using `IHttpClientFactory`.

Each destination has its own local name and HTTP client configuration.
The library targets .NET 10.

## Register a destination

Reference this package and register an HTTP destination through
`AddPingTrace`:

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

Replace the example address with the remote application's address.

The outgoing operation depends on the requested trace:

| Operation | Default request | Expected response |
| --- | --- | --- |
| Named destination Trace | `GET /pingtrace/Trace` | One named `TraceResult` object |
| TraceAll | `POST /pingtrace/TraceAll` with traversal state | A nonempty array of named trace results |
| Discover | `POST /pingtrace/Discover` with discovery state | A validated `DiscoveryResult` |
| Routed Trace | `POST /pingtrace/TraceRoute` with route state | A nonempty array of named trace results |

Registration does not send a request. Communication occurs when the
application traces the registered destination.

## Expose the calling application's endpoints

For an ASP.NET Core application, also reference `PingTrace.AspNetCore`:

```csharp
using PingTrace.Adapter.HttpClient;
using PingTrace.AspNetCore;
using PingTrace.Runtime;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddPingTrace(pingTrace =>
{
    pingTrace.AddHttpDestination("ApiService", client =>
    {
        client.BaseAddress = new Uri("https://api.example.com/");
    });
});

WebApplication app = builder.Build();

app.MapPingTrace();

app.Run();
```

Then request:

```http
GET /pingtrace/Trace?destination=ApiService
```

With the default runtime, the response contains:

```text
Calling application's result
Remote application's result
```

The adapter returns only the remote result. The runtime adds and completes
the calling application's result.

Configure authorization in the hosting applications as appropriate for the
diagnostic information being exposed.

## Destination names and HTTP clients

The destination name is the owner's alias. After validating a diagnostic response,
the connector assigns this name to its first result. Descendant names, outcomes,
metadata, and timings remain unchanged. This distinguishes instances of the same
application registered as separate destinations.

Each registration creates:

- A named HTTP client using `PingTrace.Http:{name}`.
- A scoped `IPingTraceConnector` for that destination, also implementing
  `IRecursivePingTraceConnector`, `IDiscoverablePingTraceConnector`, and
  `IRoutedPingTraceConnector`.

The default runtime compares destination names without regard to case.
Names must be unique and must not conflict with the calling application's
own name.

Register each destination once.

### Existing application clients

Using an existing class name as the alias is convenient:

```csharp
builder.Services.AddPingTrace(pingTrace =>
{
    pingTrace.AddHttpDestination(nameof(WeatherApiClient), client =>
    {
        client.BaseAddress = apiServiceUri;
    });
});
```

This does not resolve `WeatherApiClient` or copy its client configuration.
The PingTrace adapter creates a separate named client.

Share the address or configuration logic explicitly when both clients should
reach the same application. Configure any required authentication handlers
for the diagnostic client as well.

There is currently no overload that accepts an existing named or typed client.

## Configure the remote paths

`AddHttpDestination` accepts optional `tracePath` and `traceAllPath` arguments:

```csharp
builder.Services.AddPingTrace(pingTrace =>
{
    pingTrace.AddHttpDestination(
        "ApiService",
        client =>
        {
            client.BaseAddress = new Uri("https://api.example.com/");
        },
        tracePath: "/diagnostics/Trace",
        traceAllPath: "/diagnostics/TraceAll");
});
```

Use this when the remote application maps PingTrace under a custom prefix.
The paths are independent: setting `tracePath` does not change `traceAllPath`.

### Applications hosted beneath a path

Both default paths begin with `/`, so they resolve from the server root.

To preserve a path in `BaseAddress`, give the base address a trailing slash
and supply a trace path without a leading slash:

```csharp
builder.Services.AddPingTrace(pingTrace =>
{
    pingTrace.AddHttpDestination(
        "ApiService",
        client =>
        {
            client.BaseAddress = new Uri("https://example.com/backend/");
        },
        tracePath: "pingtrace/Trace",
        traceAllPath: "pingtrace/TraceAll");
});
```

The resulting requests target:

```text
https://example.com/backend/pingtrace/Trace
https://example.com/backend/pingtrace/TraceAll
```

| Base address | Trace path | Request address |
| --- | --- | --- |
| `https://example.com/backend/` | `/pingtrace/Trace` | `https://example.com/pingtrace/Trace` |
| `https://example.com/backend/` | `pingtrace/Trace` | `https://example.com/backend/pingtrace/Trace` |

All endpoint paths must be nonblank. Registration rejects absolute URLs, paths
beginning with `//`, and paths containing backslashes.

## Configure client behavior

The client configuration callback can set properties such as the base address,
timeout, and default headers:

```csharp
builder.Services.AddPingTrace(pingTrace =>
{
    pingTrace.AddHttpDestination("ApiService", client =>
    {
        client.BaseAddress = new Uri("https://api.example.com/");
        client.Timeout = TimeSpan.FromSeconds(5);
    });
});
```

The timeout shown here is an application choice, not an adapter default.
During TraceAll, the runtime also supplies a cancellation token linked to the
traversal deadline. A longer client timeout does not extend that deadline.

`AddHttpDestination` returns `IHttpClientBuilder`, allowing the application to
configure HTTP handlers and other client services through standard client
registration extensions.

Authentication, resilience policies, and service discovery are configured by
the application. The adapter does not add them itself.

## Aspire service discovery

The repository's Aspire sample uses:

```csharp
Uri apiServiceUri = new("https+http://apiservice");

builder.Services.AddPingTrace(pingTrace =>
{
    pingTrace.AddHttpDestination(nameof(WeatherApiClient), client =>
    {
        client.BaseAddress = apiServiceUri;
    });
});
```

This address depends on the service discovery configured by the sample's
shared service defaults and the resource configuration supplied by its
AppHost.

Setting this URI alone does not configure service discovery.

The diagnostic client can receive application-wide HTTP client defaults,
but it does not inherit settings specific to the sample's typed
`WeatherApiClient`.

## Remote response contract

For direct destination Trace, the adapter deserializes one `TraceResult`.

It requires:

- A successful HTTP response.
- JSON that can be deserialized into `TraceResult`.
- A non-null result with a nonblank `Name`.

It returns that result as a one-element collection for the runtime.

The adapter does not validate every result field. For example, it does not
require a particular machine name, identity, payload, or elapsed value.

A destination Trace endpoint returns an array, so it is not the response
shape expected by `TraceAsync`. Configure `tracePath` to the remote application's
local Trace endpoint.

For TraceAll, `traceAllPath` must return a nonempty array of `TraceResult` objects.
Every entry must be non-null and have a nonblank name. The adapter returns the
remote subtree without adding the caller's local result; the runtime adds it.

POST serializes the supplied `TraceAllRequest`, preserving depth, deadline, and
ancestors. The remote application continues traversal using that context. A
missing recursive endpoint remains an HTTP failure; there is no fallback to
local Trace that could silently omit descendants.

## Failures and cancellation

| Condition | Behavior |
| --- | --- |
| `BaseAddress` is missing | Returns a failed `Configuration` result. |
| Response JSON is invalid or incompatible | Returns an `InvalidResponse` result when parsing throws `JsonException`. |
| Deserialized result is null or has a blank name | Returns a failed `InvalidResponse` result. |
| Recursive result array is null, empty, or contains a null or unnamed entry | Returns a failed `InvalidResponse` result. |
| Remote HTTP status is unsuccessful | Returns `Authentication` for 401, `Authorization` for 403, or `RemoteError` for other error statuses. |
| HTTP transport failure | Returns a failed `Connection` result identifying the configured destination. |
| HttpClient timeout | Returns a failed `Timeout` result. |
| Caller cancellation or traversal deadline exhaustion | Cancellation propagates and stops the operation. |
| Unexpected exception | Propagates to the caller. |

The connector checks cancellation before creating the client and passes the
token to the HTTP and JSON operation.

Diagnostic failures use the same TraceResult shape as successful traces,
with `Status = Failed`, a `FailureKind`, a stable `ErrorCode`, and an explanation
in `Message`. `Payload` is reserved for diagnostic evidence. Remote outcomes are
preserved, including `Unknown` from older producers that omit status. A completed
HTTP request does not imply that every destination is healthy.

Each locally generated failure is logged at Warning level with its destination,
error code, and message. Handled exceptions are attached to the log entry.
The host must configure a logging provider and appropriate filtering to retain
these details; registering logging services alone does not store logs.

A client is obtained from `IHttpClientFactory` for each trace operation and
disposed when that operation finishes. Handler lifetime management remains
with the factory.

## Timing and traversal

With the default runtime, the calling application's elapsed time includes
the adapter operation, including the HTTP request and response processing.

The remote result contains the remote application's own timing.
Neither value directly measures network latency between the applications.

Named destination Trace requests the remote application's local Trace.
TraceAll requests recursive tracing, allowing the remote application to visit
its own registered dependencies. Neither operation forwards the destination
alias or discovers arbitrary dependencies.

Recursive results are flat and ordered. Shared dependencies may appear through
multiple paths, and parent durations include descendant work. Do not add those
overlapping durations to calculate total latency.

## Learn more

- [PingTrace overview](https://github.com/patware/Patware.PingTrace/blob/main/README.md)
- [Runtime registration and behavior](https://github.com/patware/Patware.PingTrace/blob/main/src/PingTrace.Runtime/README.md)
- [ASP.NET Core endpoints](https://github.com/patware/Patware.PingTrace/blob/main/src/PingTrace.AspNetCore/README.md)
- [Distributed hosting](https://github.com/patware/Patware.PingTrace/blob/main/docs/architecture/DISTRIBUTED-HOSTING.md)
- [Connector architecture](https://github.com/patware/Patware.PingTrace/blob/main/docs/architecture/PLUGIN-ARCHITECTURE.md)

## Discovery and routed requests in 0.3.0

The connector supports Discover and TraceRoute in addition to Trace and TraceAll.
Discovery failures handled by the adapter produce incomplete reports with
branch issues; an unavailable endpoint does not prove that descendants are absent.
Routed requests propagate recognized `PingTrace.Routing.v1` execution errors as
`TraceRoutingException`. Other HTTP errors use the existing diagnostic-failure
handling. A generic HTTP 404 is not classified as an unknown destination.

Discovery and routed responses use a 1 MiB buffering limit. Caller cancellation
and unexpected exceptions propagate. The first routed diagnostic result receives
the owner's alias, while descendant names and evidence are preserved.

Registration retains the original five-parameter overload, adds a six-parameter
overload for an explicit discovery path, and a seven-parameter overload accepting
all four endpoint paths. Additional overloads require explicit path arguments.

For a remote application mapped under `/diagnostics`, configure all four paths:

```csharp
pingTrace.AddHttpDestination(
    name: "ApiService",
    configureClient: client => client.BaseAddress = new Uri("https://api.example.com/"),
    tracePath: "/diagnostics/Trace",
    traceAllPath: "/diagnostics/TraceAll",
    discoverPath: "/diagnostics/Discover",
    traceRoutePath: "/diagnostics/TraceRoute");
```

These paths are independent. The shorter overloads retain the default discovery
and/or routed paths; a custom trace prefix is not inferred for other operations.

See the [routing guide](https://github.com/patware/Patware.PingTrace/blob/main/docs/architecture/DISCOVERY-AND-ROUTING.md).
