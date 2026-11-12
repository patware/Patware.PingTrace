# Execution model

This page describes how the current default runtime processes Ping,
local Trace, destination Trace, and recursive TraceAll requests.
TraceAll visits explicitly registered dependencies. Local Trace stays local;
named Trace can discover an indirect destination in 0.3.0.

Examples use the default `/pingtrace` route prefix and default ASP.NET Core
JSON settings. Hosts can change the route prefix, serialization settings,
and authorization policy.

## Operations at a glance

| Request | Dependency calls | Successful response |
| --- | --- | --- |
| `GET /pingtrace/Ping` | None | Plain-text `Pong` |
| `GET /pingtrace/Trace` | None | One local trace object |
| `GET /pingtrace/Trace?destination=LOCAL_NAME` | None | Array containing one local result |
| `GET /pingtrace/Trace?destination=DEPENDENCY_NAME` | Selected connector only | Array containing the local result followed by connector results |
| `GET /pingtrace/TraceAll` | All registered destinations, recursively where supported | JSON array containing the local result followed by dependency results |
| `POST /pingtrace/TraceAll` | Continues traversal using the supplied request body | The same recursive result array |
| `GET /pingtrace/Discover` | Contacts discoverable participants; no leaf probes | Discovery report |
| `POST /pingtrace/Discover` | Continues discovery with context | Discovery report |
| `POST /pingtrace/TraceRoute` | Selected connector path only | JSON trace array |

TraceAll is separate from discovery. It executes diagnostic probes; Discover
lists registered routes without executing leaf probes.

## Before requests arrive

Service registration and endpoint mapping have separate responsibilities:

```csharp
builder.Services.AddPingTrace();
```

Registers the default runtime with dependency injection.

```csharp
app.MapPingTrace();
```

Maps incoming HTTP requests to the registered `IPingTrace` implementation.

For dependencies, the registration callback adds connectors:

```csharp
builder.Services.AddPingTrace(pingTrace =>
{
    pingTrace.AddHttpDestination("ApiService", client =>
    {
        client.BaseAddress = new Uri("https://api.example.com/");
    });
});
```

The callback runs during registration. It does not contact the dependency.

When the default runtime is resolved, it receives the registered connectors
and validates their names. Invalid connector registrations can therefore
prevent even a local operation from executing.

## Ping

Request:

```http
GET /pingtrace/Ping
```

Successful response:

```http
HTTP/1.1 200 OK
Content-Type: text/plain; charset=utf-8

Pong
```

The endpoint resolves `IPingTrace` and calls `Ping()`.

The default implementation returns a constant string. It does not create
trace results or invoke connector tracing methods.

Use this operation when the question is whether the application endpoint
responds. A successful response does not establish dependency availability.

## Local Trace

Request:

```http
GET /pingtrace/Trace
```

The absence of the `destination` query parameter selects local Trace.

The default runtime:

1. Checks cancellation.
2. Creates a result, capturing its start time.
3. Populates the application name, machine name, and runtime identity.
4. Calls `Finish` with the entry-assembly version, falling back to `Pong`, and captures the finish time.
5. Returns the result.

Example successful JSON response:

```json
{
  "name": "AspireAppSample.Web",
  "identity": "EXAMPLE\\web-service",
  "machineName": "WEB01",
  "startedAt": "2026-10-11T10:00:00.000-04:00",
  "finishedAt": "2026-10-11T10:00:00.001-04:00",
  "payload": "1.2.3.0",
  "elapsed": "00:00:00.0010000"
}
```

The response has status 200 and an `application/json` media type.

Values above are illustrative. Actual identity formatting and timestamp
offsets depend on the execution environment.

Version numbers in these examples are illustrative. Each application's
default payload comes from its own managed entry assembly, with `Pong`
used when its version is unavailable.

No dependency is contacted, even when connectors are registered.

## Named local Trace

Request:

```http
GET /pingtrace/Trace?destination=AspireAppSample.Web
```

If the destination matches the local application's name, ignoring case,
the runtime performs local Trace and wraps its result in a collection.

Example successful response:

```json
[
  {
    "name": "AspireAppSample.Web",
    "identity": "EXAMPLE\\web-service",
    "machineName": "WEB01",
    "startedAt": "2026-10-11T10:00:00.000-04:00",
    "finishedAt": "2026-10-11T10:00:00.001-04:00",
    "payload": "1.2.3.0",
    "elapsed": "00:00:00.0010000"
  }
]
```

The payload remains the local application version. The destination suffix
is added only when the runtime invokes a dependency connector.

The current difference between local-object and destination-array responses
is intentional in the implemented contract. Consumers must handle it.

A consistent collection response is planned but not implemented.

## Dependency Trace

In the Aspire sample, the frontend registers the API dependency under
the name `WeatherApiClient`.

Request to the frontend:

```http
GET /pingtrace/Trace?destination=WeatherApiClient
```

The runtime:

1. Checks cancellation and validates the destination argument.
2. Looks up the connector by name, ignoring case.
3. Creates the local result.
4. Awaits the connector's `TraceAsync` method.
5. Places the local result first and appends the connector results.
6. Completes the local result.
7. Returns the collection.

Example successful response:

```json
[
  {
    "name": "AspireAppSample.Web",
    "identity": "EXAMPLE\\web-service",
    "machineName": "WEB01",
    "startedAt": "2026-10-11T10:00:00.000-04:00",
    "finishedAt": "2026-10-11T10:00:00.025-04:00",
    "payload": "1.2.3.0 --> [WeatherApiClient]",
    "elapsed": "00:00:00.0250000"
  },
  {
    "name": "WeatherApiClient",
    "identity": "EXAMPLE\\api-service",
    "machineName": "API01",
    "startedAt": "2026-10-11T10:00:00.010-04:00",
    "finishedAt": "2026-10-11T10:00:00.011-04:00",
    "payload": "2.0.0.0",
    "elapsed": "00:00:00.0010000"
  }
]
```

The local alias `WeatherApiClient` selects the connector and replaces the first
remote result's name. Descendant names belong to their respective owners.

Only the selected connector is invoked. Other registered dependencies
are not traced.

## What the HTTP connector does

For named destination Trace, the built-in HTTP connector:

1. Checks cancellation.
2. Creates its named client through `IHttpClientFactory`.
3. Requires a configured base address.
4. Requests its configured trace path.
5. Deserializes one `TraceResult`.
6. Rejects a null result or an empty result name.
7. Returns a collection containing that result.

The default request to the dependency is:

```http
GET /pingtrace/Trace
```

The connector does not append the frontend's destination name to that request.

Consequently, the remote application performs local Trace. Its own
dependencies are not invoked.

A custom connector may return multiple results. The runtime preserves
their order but does not infer a graph from them.

## Destination interpretation

Destination names are application-owned aliases for registered connectors.

Matching is case-insensitive but does not trim surrounding whitespace.
For example, `WeatherApiClient` and `weatherapiclient` select the same
connector; ` WeatherApiClient ` does not.

Supply one destination query value and URL-encode it when necessary.

The current handler combines repeated query values into a string before
lookup. Repeating `destination` does not request multiple traces.

A destination value is an alias, not an arbitrary URL or textual path expression.
If it is not local or immediate, the runtime resolves it through discovery.
Use TraceRoute with an array of segments to select an explicit path.

## Recursive TraceAll

TraceAll starts a local trace, visits the application's registered
destinations, and completes the local trace after those destinations finish.

Start a traversal through HTTP:

```http
GET /pingtrace/TraceAll
```

Or through an injected `IPingTrace`:

```csharp
IReadOnlyList<TraceResult> results = await pingTrace.TraceAllAsync(
    cancellationToken: cancellationToken);
```

### Traversal and ordering

The default runtime visits destinations sequentially in
case-insensitive ordinal order by their registered names.

For each destination:

- An `IRecursivePingTraceConnector` receives `TraceAllAsync` with the
  remaining depth, deadline, and ancestor path.
- A connector implementing only `IPingTraceConnector` receives its existing
  `TraceAsync` call and is treated as a leaf.

Sequential execution avoids concurrent operations against a shared scoped
DbContext.

Each application returns its local result first, followed by the results
from each destination. The runtime preserves each connector's result order.
With the built-in adapters, this produces depth-first traversal.

A shared dependency is visited once through each path that reaches it.
Results are not globally deduplicated.

### HTTP and database destinations

The HTTP adapter sends a POST request to the remote application's
`/pingtrace/TraceAll` endpoint. Its JSON body contains the traversal state.
The remote application then traces its own registered destinations.

The response must be a nonempty JSON array of non-null trace results with
nonblank names. An unavailable recursive endpoint remains a failure; the
adapter does not fall back to local Trace. An HttpClient timeout returns a diagnostic
entry stating that the destination did not respond before the HTTP timeout.

The Entity Framework adapter is a leaf. TraceAll invokes its existing
SQL Server diagnostic query.

In the Aspire sample, requesting the frontend's `/pingtrace/TraceAll`
reaches the API service and then its `ActiveDirectory`, `ApiServiceDb`, and
`MicrosoftGraph` destinations. AD and Graph are simulated in the sample.

Named destination Trace retains its narrower behavior:
`/pingtrace/Trace?destination=WeatherApiClient` requests the API's local
Trace and does not continue to its database.

### Traversal limits

`TraceAllRequest` carries the state of one traversal path:

| Property | Default | Meaning |
| --- | --- | --- |
| `RemainingDepth` | `8` | Connector hops still permitted |
| `DeadlineUtc` | UTC time at request construction plus 30 seconds | Latest permitted execution time |
| `Ancestors` | Empty array | Runtime node identities already visited on this path |

A new call without a request creates a fresh `TraceAllRequest`.

Before calling a recursive connector, the runtime decrements the depth and
appends its node identity to a new ancestor array. Sibling paths receive
independent arrays.

Validation requires a depth from zero through sixteen, a non-null ancestor
array containing at most sixteen nonempty identifiers, and a combined
ancestor count plus remaining depth no greater than sixteen. The deadline
must not be the default timestamp.

At zero depth, an application without registered destinations returns its
local result. An application with destinations throws rather than silently
returning an incomplete traversal.

Each runtime uses the earlier of the received deadline and thirty seconds
after its own execution begins. That effective deadline is forwarded to
descendants. A caller can shorten the budget; a later supplied deadline
cannot extend a runtime invocation beyond its local thirty-second limit.

For example:

```csharp
TraceAllRequest request = new()
{
    RemainingDepth = 4,
    DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(10)
};

IReadOnlyList<TraceResult> results = await pingTrace.TraceAllAsync(
    request,
    cancellationToken);
```

Create the request when starting the operation. Reusing a request later
also reuses its original deadline.

Deadlines use wall-clock time, so participating hosts should have
synchronized clocks. Cancellation is cooperative: a connector that ignores
its token cannot be forcibly stopped by the runtime.

Depth and time limits do not impose a separate maximum on the number of
requests or results in a wide dependency graph.

### Cycle detection

`AddPingTrace` registers `TraceAllNode` as a singleton and `DefaultPingTrace`
as a scoped service. Separate request scopes therefore recognize the same
running application instance.

If the current node already appears in the incoming ancestor path,
traversal throws `InvalidOperationException` before invoking connectors.

Node identities are transient runtime identifiers, not application names
or permanent identifiers in trace results. Separate application instances
have different identities. The depth limit remains necessary when a loop
passes through different instances behind a load balancer.

### Results and timing

A leaf application's payload contains its entry-assembly version, falling
back to `Pong`. An application with destinations uses
`{version} --> [TraceAll]`.

The local result starts before dependency execution and finishes afterward.
Its elapsed time therefore includes descendant work. Parent and child
durations overlap and must not be added to calculate total latency.

Results remain a flat collection. They do not contain parent identifiers
or enough relationship information to reconstruct a branching graph
reliably.

### Failures and cancellation

HTTP transport failures, unsuccessful HTTP responses, and HttpClient
timeouts produce diagnostic entries. Traversal continues to subsequent
destinations while its deadline permits.

Exceptions propagated by connectors stop traversal without returning
the accumulated results. Cycles and exhausted depth throw
InvalidOperationException. Caller cancellation and expired traversal
deadlines produce cancellation exceptions.

All Trace endpoints pass `HttpContext.RequestAborted` to the implementation.
The runtime links that token to its execution deadline.

The recursive POST endpoint validates the supplied request before invoking
the runtime. Invalid traversal state is returned as HTTP 400.
Request-body binding handles malformed or missing JSON.

Other failures propagate to the host's exception handling. PingTrace does
not define a dedicated HTTP status or error body for cycles, exhausted
depth, or deadline expiry. Exceptions crossing HTTP boundaries are observed
through the remote HTTP response rather than preserving their original
.NET exception type.

## Errors

### Empty destination

Request:

```http
GET /pingtrace/Trace?destination=
```

Response status: 400.

With default JSON serialization, the response body is a JSON string:

```json
"Destination must not be empty."
```

A whitespace-only destination also produces status 400.

An empty parameter does not mean local Trace. Omit the parameter to request
local Trace.

### Unknown destination

A destination without a local or direct match triggers discovery. Only complete
discovery with no match produces HTTP 404 and a structured response:

```json
{
  "protocol": "PingTrace.Routing.v1",
  "errorCode": "Routing.DestinationNotFound",
  "message": "Complete discovery found no destination named 'Missing'.",
  "candidates": [],
  "issues": []
}
```

Multiple routes produce HTTP 409. Incomplete discovery produces HTTP 503 unless
multiple matches already establish ambiguity. These are `TraceRoutingException`
failures in process. The legacy `KeyNotFoundException` catch remains for custom
implementations and returns the older generic JSON string.

See [discovery and routing](DISCOVERY-AND-ROUTING.md) for endpoint contracts,
resolution precedence, explicit routes, limits, and compatibility.

### Dependency failure

The HTTP adapter returns diagnostic entries for transport failures,
unsuccessful HTTP responses, and HttpClient timeouts.

Malformed JSON, invalid remote trace results, and a missing HTTP client
base address still produce exceptions that propagate to the host's
exception handling.

A remote HTTP 404 produces a diagnostic entry containing that status.
It does not mean the frontend's destination alias is unknown, and the
remote status is not copied onto the frontend response.

When a connector throws an exception, the default runtime stops
traversal without returning the accumulated results.

## Cancellation

The HTTP endpoint passes `HttpContext.RequestAborted` to the runtime.

The runtime checks cancellation before executing a trace and passes the
token to the selected connector. The HTTP connector passes it to the
outgoing request and JSON-reading operation.

Cancellation propagates as an exception. PingTrace does not assign it
a dedicated HTTP response status or guarantee that a disconnected caller
receives a response.

Custom connectors must cooperate with cancellation. Passing a token does
not forcibly terminate work that ignores it.

## Timing interpretation

A result's constructor records `StartedAt`. Calling `Finish` records
`FinishedAt` and sets the payload.

`Elapsed` is calculated as:

```text
FinishedAt - StartedAt
```

For a dependency trace, the frontend's elapsed time includes awaiting the
connector. The remote application's elapsed time measures its local
diagnostic work.

These intervals overlap. Do not add them to calculate total duration.

The difference between frontend and remote elapsed times is not a pure
network-latency measurement. It can include client setup, authentication,
serialization, queuing, retries, and other processing.

The current timestamps use `DateTime.Now`. Clock adjustments can affect
duration calculations, and clocks on different machines need not agree.
Do not infer transit time by subtracting timestamps from different machines.

The result interval also excludes some HTTP work: it begins inside the
runtime and ends before final response serialization.

## Verify the Aspire sample

With the sample running, use the frontend address shown by Aspire.

1. Request `/pingtrace/Ping` and confirm `Pong`.
2. Request `/pingtrace/Trace` and inspect the frontend's local result.
3. Request `/pingtrace/Trace?destination=WeatherApiClient`.
4. Confirm that the array contains the frontend result followed by the
   API result.
5. Request `/pingtrace/Trace` directly from the API and compare its
   local observations.
6. Request `/pingtrace/TraceAll` from the frontend to include the API's
   configured `ActiveDirectory`, `ApiServiceDb`, and `MicrosoftGraph` diagnostics.
   AD and Graph are simulated; the database must be available for traversal to complete.

Use the reported application name and machine to verify which component
answered. Use local versus dependency elapsed times to narrow a delay
investigation.

These requests verify the configured diagnostic path. They do not execute
the weather operation or establish that all application behavior is healthy.

## Related documentation

- [Architecture](ARCHITECTURE.md)
- [Definitions](PINGTRACE-DEFINITIONS.md)
- [Extension contracts](PLUGIN-ARCHITECTURE.md)
- [Distributed hosting](DISTRIBUTED-HOSTING.md)
- [Security](SECURITY.md)
- [Capability boundaries](../design/CAPABILITY-BOUNDARIES.md)
