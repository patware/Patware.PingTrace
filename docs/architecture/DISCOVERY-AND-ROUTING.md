# Discovery and routed tracing

Version 0.3.0 adds discovery of registered destinations and tracing through
intermediate applications. It does not scan the network or require a central
registry. Each application owns its immediate connector registrations.

## Discover before probing

`GET /pingtrace/Discover` starts discovery. `POST /pingtrace/Discover` continues
it with a `DiscoveryRequest` containing `RemainingDepth`, `DeadlineUtc`, and
`Ancestors`. Discovery contacts participating applications but does not run
their leaf diagnostic probes or produce diagnostic timings and payloads.

For the Aspire frontend, a complete response can be:

```json
{
  "applicationName": "AspireAppSample.Web",
  "isComplete": true,
  "routes": [
    { "destination": "RedisCache", "segments": ["RedisCache"] },
    { "destination": "WeatherApiClient", "segments": ["WeatherApiClient"] },
    { "destination": "ActiveDirectory", "segments": ["WeatherApiClient", "ActiveDirectory"] },
    { "destination": "ApiServiceDb", "segments": ["WeatherApiClient", "ApiServiceDb"] },
    { "destination": "MicrosoftGraph", "segments": ["WeatherApiClient", "MicrosoftGraph"] }
  ],
  "issues": []
}
```

`ApplicationName` identifies the application producing the report. Each route's
`Destination` and `Segments` use registered aliases. A discovered destination is
configured, not necessarily healthy or reachable when subsequently traced.

Immediate registrations are listed first. Discoverable connectors are expanded
sequentially in case-insensitive name order. Leaf connectors are listed without
being invoked. A recursive connector without discovery support produces an
incomplete branch rather than an assertion that it has no descendants.

HTTP communication failures, client timeouts, invalid responses, cycles, and
depth or response limits can produce `IsComplete = false` with branch-relative
issues. Parents prefix issue paths with their connector alias. HTTP 200 from
Discover does not imply completeness; inspect `IsComplete` and `Issues`.
Caller cancellation, deadline exhaustion, and unexpected exceptions propagate.

## Trace an indirect destination

From the frontend, request:

```http
GET /pingtrace/Trace?destination=ActiveDirectory
```

The default runtime first checks its local application name and immediate
registrations. If neither matches, it discovers routes and compares destination
names case-insensitively. Identical routes are deduplicated. Multiple distinct
matches are ambiguous. Otherwise, incomplete discovery prevents resolution,
even if one matching route was found. Complete discovery must yield one match.

The selected path is `WeatherApiClient -> ActiveDirectory`. The frontend forwards
the remaining route to the API, which invokes its own Active Directory connector.
Redis, SQL Server, and Graph diagnostic probes are not executed by this trace.

Discovery and execution share a thirty-second deadline. Discovery consumes this
budget but is excluded from diagnostic result timings. A failed diagnostic does
not trigger rediscovery, retries by the runtime, or selection of another branch.
Application-configured HTTP handlers can still impose their own retry policies.

## Execute an explicit route

`POST /pingtrace/TraceRoute` accepts `TraceRouteRequest`. For example, using a
fresh deadline and an empty ancestor path:

```csharp
using System.Net.Http.Json;
using PingTrace.Core;

TraceRouteRequest request = new()
{
    Segments = ["WeatherApiClient", "ActiveDirectory"]
};

using HttpResponseMessage response = await client.PostAsJsonAsync(
    "/pingtrace/TraceRoute", request, cancellationToken);
```

An explicit route bypasses discovery. Every application consumes its immediate
connector segment and forwards the remainder. An empty route requests local
Trace and returns it as an array. A final connector segment invokes that
connector's ordinary Trace operation. Missing segments and unsupported forwarding
produce structured routing errors; they do not trigger an alternative search.

## Names and result interpretation

The owner controls a destination's name through registration. An HTTP connector
renames only the first remote result to its own alias, preserving descendant
names and all diagnostic evidence. Two instances reporting the same application
name can therefore appear as `Left` and `Right` when registered under those names.

The example routed trace returns names in this order:

```text
AspireAppSample.Web
WeatherApiClient
ActiveDirectory
```

The original request's local root retains its application name. Names are not
globally unique, and trace results remain flat collections without route fields.
Discovery paths distinguish branches; a repeated descendant name in TraceAll
alone does not encode its parent relationship.

`Status` describes each result's own operation. A successful parent or HTTP 200
does not establish that descendants succeeded. Inspect `FailureKind`, `ErrorCode`,
and `Message` on failed results. `Payload` remains diagnostic evidence.

## Routing errors

In-process calls throw `TraceRoutingException` with an `Error` property. ASP.NET
Core translates that error into JSON with protocol `PingTrace.Routing.v1`:

| Error code | HTTP status | Meaning |
| --- | --- | --- |
| `Routing.DestinationNotFound` | 404 | Complete discovery found no matching destination. |
| `Routing.SegmentNotRegistered` | 404 | An explicit route references an unregistered immediate segment. |
| `Routing.AmbiguousDestination` | 409 | More than one distinct route matches the destination. |
| `Routing.Cycle` | 409 | Explicit execution revisits an ancestor application instance. |
| `Routing.DiscoveryIncomplete` | 503 | Discovery cannot conclusively resolve the requested name. |
| `Routing.ForwardingNotSupported` | 501 | The selected participant cannot forward the remaining route. |

Errors contain `Protocol`, `ErrorCode`, `Message`, `Candidates`, and `Issues`.
Candidates and issues are populated where relevant to resolution. A generic
remote HTTP 404 is not evidence of a missing registered route: the HTTP adapter
recognizes execution errors only when their protocol, code, and status agree.
Other HTTP diagnostic failures become failed trace results.

Malformed request state returns HTTP 400 before execution. Custom implementations
without the requested discovery or routing capability receive HTTP 501. The
named Trace endpoint retains its legacy `KeyNotFoundException` mapping for custom
implementations; the default runtime uses structured routing exceptions.

## Limits and compatibility

- Custom HTTP endpoint prefixes require matching `tracePath`, `traceAllPath`,
  `discoverPath`, and `traceRoutePath` registration arguments.
- Requests default to eight connector hops and a deadline thirty seconds after
  construction. Validation allows at most sixteen combined ancestor entries and
  remaining hops. Construct a fresh request for each operation.
- Each runtime uses the earlier of the incoming deadline and its local
  thirty-second cap. Deadlines cross machines; keep their clocks synchronized.
- Ancestors identify application instances through the runtime's shared node.
  Cycle detection applies to the current path, allowing shared dependencies in
  separate branches.
- Discovery reports contain at most 256 routes and 64 issues. HTTP discovery and
  routed responses are buffered with a 1 MiB limit.
- Cancellation is cooperative; it cannot forcibly stop synchronous LDAP calls
  or other uncooperative operations.
- Discovery is performed on demand and is not cached. Topology can change before
  execution. There is no central registry or compile-time transitive topology.
- Discovery and routing are optional capabilities. The existing `IPingTrace` and
  leaf connector contracts remain usable. Older HTTP peers may support direct
  Trace while lacking the endpoints needed for indirect resolution.

## In-process capabilities and custom connectors

Resolve `IPingTrace` and check whether it implements `IPingTraceDiscovery` or
`IPingTraceRouter`. `AddPingTrace` registers the default implementation as
`IPingTrace`; it does not separately register these optional interfaces.

`IDiscoverablePingTraceConnector` returns routes relative to its remote
application. `IRoutedPingTraceConnector` forwards route state that already excludes
its own consumed alias. Implement these alongside `IRecursivePingTraceConnector`
when a connector supports all three operations. Preserve deadlines and ancestor
state rather than starting a new traversal at each hop.

## Protect topology and diagnostics

All new endpoints belong to the group returned by `MapPingTrace()`, so
`app.MapPingTrace().RequireAuthorization()` protects them with the other endpoints.
Discovery exposes registered topology; routing errors can expose candidate paths
and branch issues. Configure authentication on each outgoing HTTP client as well
as authorization at the receiving application. Discovery does not confer access
to execute a diagnostic operation.
