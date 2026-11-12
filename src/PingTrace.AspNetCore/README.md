# PingTrace.AspNetCore

**Expose application Ping and Trace diagnostics through ASP.NET Core endpoints.**

`PingTrace.AspNetCore` maps HTTP requests to an application's `IPingTrace`
implementation.

Use these endpoints for deployment verification, monitoring, and investigating
which application and dependency responded.

The library targets .NET 10.

## Get started

Reference `PingTrace.Runtime` and `PingTrace.AspNetCore` in your ASP.NET Core
application:

```csharp
using PingTrace.AspNetCore;
using PingTrace.Runtime;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddPingTrace();

WebApplication app = builder.Build();

app.MapPingTrace();

app.Run();
```

`AddPingTrace`, supplied by `PingTrace.Runtime`, registers the default
implementation. `MapPingTrace` exposes that implementation over HTTP.

Applications can instead register a custom `IPingTrace` implementation.
Endpoint mapping requires an implementation to be available through dependency
injection; mapping alone does not register one.

## Endpoints

The default route prefix is `/pingtrace`.

| Request | Successful response |
| --- | --- |
| `GET /pingtrace/Ping` | Plain-text response from `IPingTrace.Ping()`. |
| `GET /pingtrace/Trace` | One JSON trace object. |
| `GET /pingtrace/Trace?destination=NAME` | A JSON array of trace results. |
| `GET /pingtrace/TraceAll` | Starts a recursive traversal and returns a JSON array. |
| `POST /pingtrace/TraceAll` | Continues traversal from a JSON `TraceAllRequest` and returns a JSON array. |
| `GET /pingtrace/Discover` | Starts discovery; returns routes, completeness, and issues. |
| `POST /pingtrace/Discover` | Continues discovery with request context. |
| `POST /pingtrace/TraceRoute` | Executes an explicit route and returns a trace array. |

Successful responses use HTTP status `200`.

The default runtime returns `Pong` for Ping. A custom implementation controls
its own Ping response.

### Ping

```http
GET /pingtrace/Ping
```

With the default runtime, the response body is:

```text
Pong
```

The response media type is `text/plain`.

Ping confirms that the request reached the application endpoint and its
implementation responded. The default implementation does not invoke
dependencies.

### Local Trace

```http
GET /pingtrace/Trace
```

When the `destination` query parameter is absent, the endpoint invokes the
local Trace overload.

The response is one JSON object representing a `TraceResult`, including the
application name, machine name, execution identity, timestamps, elapsed time,
and diagnostic payload.

With the default runtime, local Trace does not invoke dependencies.

### Destination Trace

```http
GET /pingtrace/Trace?destination=ApiService
```

When `destination` is present and nonblank, the endpoint invokes the named
Trace overload.

The response is a JSON array. With the default runtime, it contains the local
result followed by results from the selected connector.

If the destination matches the local application's name, the default runtime
returns an array containing only the local result.

The object-versus-array distinction depends on the presence of the destination
parameter, not on the number of returned results.

URL-encode destination names when constructing requests.

### Recursive TraceAll

GET starts traversal using the runtime defaults: eight connector hops and a
thirty-second cooperative deadline. POST accepts the remaining depth, absolute
deadline, and ancestor path forwarded by another application. Both return an
array even when the application has no destinations.

The runtime invokes recursive connectors with that context and other connectors
as leaves. Cycles, exhausted depth, cancellation, and dependency failures stop
traversal rather than returning partial success. See the
[execution guide](https://github.com/patware/Patware.PingTrace/blob/main/docs/architecture/PINGTRACE.md#recursive-traceall).

The `destination` query parameter applies to Trace, not TraceAll.

## Register an HTTP dependency

Also reference `PingTrace.Adapter.HttpClient`:

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

Replace the example address with the API's address. The remote application
must expose a compatible local Trace endpoint for named tracing and a recursive
TraceAll endpoint for recursive tracing.

Requesting `/pingtrace/Trace?destination=ApiService` invokes that dependency.
The built-in HTTP adapter requests `/pingtrace/Trace` on the remote application;
it does not automatically traverse the remote application's dependencies.

The destination alias belongs to the calling application and becomes the first
remote result's name. Descendant names remain those assigned by their owners.

## Change the route prefix

Pass a custom prefix when mapping endpoints:

```csharp
app.MapPingTrace("/diagnostics");
```

This maps:

```text
GET /diagnostics/Ping
GET /diagnostics/Trace
GET /diagnostics/Trace?destination=NAME
GET /diagnostics/TraceAll
POST /diagnostics/TraceAll
```

That invocation does not also map the default `/pingtrace` routes.

When a remote application uses a custom prefix, configure its HTTP connector's
`tracePath` and `traceAllPath` to match the remote routes. The paths are independent.
Discovery and routed tracing also need matching `discoverPath` and
`traceRoutePath` arguments. Use the full HTTP registration overload when
customizing all four endpoint paths.

## Authorization

`MapPingTrace` returns a `RouteGroupBuilder`, allowing standard endpoint
conventions to be applied.

In an application with authentication and authorization already configured:

```csharp
app.MapPingTrace()
    .RequireAuthorization();
```

This protects Ping, Trace, TraceAll, Discover, and TraceRoute endpoints using the application's default
authorization policy.

To use an existing named policy:

```csharp
app.MapPingTrace()
    .RequireAuthorization("Diagnostics");
```

The application must register that policy and configure its authentication
scheme and middleware appropriately.

The mapping does not add authorization requirements or anonymous-access
metadata by itself. Application fallback policies and enclosing route-group
conventions can still govern access.

### Separate Ping and Trace policies

The current mapping API returns the shared group rather than separate
endpoint builders. It does not provide a built-in option to leave Ping
anonymous while requiring authorization only for Trace.

Applications needing that distinction must provide their own endpoint mapping
or another deliberate endpoint-policy configuration.

Trace responses can expose machine names, execution identities, and
application-provided payloads. Choose an authorization policy appropriate to
the deployment and keep secrets out of diagnostic payloads.

## Errors and cancellation

| Condition | Endpoint behavior |
| --- | --- |
| Destination parameter is absent | Invokes local Trace. |
| Destination is empty or whitespace | Returns `400` with the JSON string `"Destination must not be empty."`. |
| Named Trace throws `KeyNotFoundException` | Returns `404` with the JSON string `"The requested destination is not known."`. |
| TraceAll POST contains invalid traversal state | Returns `400` before invoking the runtime. |
| Other implementation or connector failure | Propagates to the host's exception handling. |

The default runtime discovers indirect destinations and reports resolution failures
through `TraceRoutingException`. The `KeyNotFoundException` catch remains for
custom implementations.

The endpoint catches that exception type from the entire named Trace call.
A custom implementation or connector throwing the same exception will
therefore also produce the `404` response.

Request-body binding handles malformed or missing JSON. An empty JSON object
uses the request type's defaults. Other failures, including cycles, exhausted
depth, and deadline expiry, do not have a package-defined error body or status
mapping. Configure exception handling in the hosting application.

All Trace and TraceAll paths pass `HttpContext.RequestAborted` to the implementation.
Downstream cancellation depends on the implementation and connector honoring
that token.

## Relationship to health checks

PingTrace complements ASP.NET Core health checks by returning diagnostic
information about the responding application and selected dependencies.

`MapPingTrace` does not register health checks, determine readiness or
liveness, or translate trace results into health-check statuses.

Keep the application's existing health-check endpoints and policies when
using PingTrace alongside them.

## Current scope

This package maps GET Ping, Trace, TraceAll, and Discover, plus POST TraceAll,
Discover, and TraceRoute.

It does not explicitly map an HTTP HEAD endpoint or provide
a graph viewer or separate Ping and Trace
authorization options.

Result contents and dependency behavior come from the registered `IPingTrace`
implementation.

## Learn more

- [PingTrace overview](https://github.com/patware/Patware.PingTrace/blob/main/README.md)
- [Runtime registration and behavior](https://github.com/patware/Patware.PingTrace/blob/main/src/PingTrace.Runtime/README.md)
- [Execution model](https://github.com/patware/Patware.PingTrace/blob/main/docs/architecture/PINGTRACE.md)
- [Security considerations](https://github.com/patware/Patware.PingTrace/blob/main/docs/architecture/SECURITY.md)

## Discovery and routing endpoints in 0.3.0

| Request | Response |
| --- | --- |
| `GET /pingtrace/Discover` | Starts discovery and returns a `DiscoveryResult`. |
| `POST /pingtrace/Discover` | Continues discovery using a `DiscoveryRequest`. |
| `POST /pingtrace/TraceRoute` | Executes a `TraceRouteRequest` and returns a trace array. |

Named Trace also resolves indirect destinations when the registered runtime
supports that behavior. Discover can return HTTP 200 with `IsComplete = false`;
callers must inspect its issues. Unsupported optional capabilities return 501.
Invalid request state returns 400 before execution.

Structured routing errors use protocol `PingTrace.Routing.v1`: missing routes
map to 404, ambiguity or cycles to 409, incomplete discovery to 503, and
unsupported forwarding to 501. These are distinct from HTTP 200 diagnostic
reports containing failed `TraceResult` entries.

All endpoints belong to the same route group and inherit its authorization.
See the [routing guide](https://github.com/patware/Patware.PingTrace/blob/main/docs/architecture/DISCOVERY-AND-ROUTING.md)
for request and error contracts.
