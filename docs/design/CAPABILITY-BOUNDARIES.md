# Capability boundaries

This page separates implemented behavior from product direction.

## Implemented

| Capability | Current behavior |
| --- | --- |
| Ping | Returns `Pong` without invoking dependencies |
| Local Trace | Returns one local `TraceResult` |
| Named local Trace | Returns a one-element collection |
| Named dependency Trace | Returns the local result followed by connector results |
| TraceAll | Sequential recursive traversal with per-path cycle checks, depth limits, and cooperative deadlines |
| Default application name | Managed entry-assembly name |
| Registration | Parameterless or a `PingTraceBuilder` callback |
| Custom implementation | An existing DI registration of `IPingTrace` is preserved |
| HTTP endpoints | GET Ping, Trace, TraceAll, Discover; POST TraceAll, Discover, TraceRoute; configurable route prefix |
| HTTP dependencies | Named clients use GET for local Trace and POST with context for TraceAll |
| Cancellation | Passed from HTTP requests through runtime and connector calls |
| Default local payload | Entry-assembly version, falling back to Pong |
| EF Core destination | SQL Server diagnostic query through a registered DbContext |
| Microsoft Graph destination | Organization metadata through a registered GraphServiceClient, with a fixed configured identity label |
| Active Directory destination | Domain diagnostics through a registered reader; included LDAP reader binds and reads RootDSE and the domain object |
| Redis destination | Ping through an application-registered IConnectionMultiplexer; deferred resolution and bounded waiting |
| Tests | Separate Runtime, AspNetCore, HttpClient, EntityFramework, MicrosoftGraph, ActiveDirectory, and Redis projects |
| Sample | Aspire frontend tracing Redis and its API, with indirect access to SQL Server and simulated Graph/AD destinations |

The Graph adapter's identity is supplied configuration metadata, not a
server-returned identity assertion. Its organization read does not verify Teams
access. A Teams-specific adapter is not currently implemented. The
sample simulation and isolated Graph tests do not establish live Microsoft
authentication or permission compatibility.

AD tests and sample simulation replace the directory reader; they do not execute
the production LDAP implementation. That implementation uses synchronous calls
and checks cancellation between operations, so a traversal deadline cannot
forcibly interrupt an executing LDAP call. Its configured identity label is not
a server-returned identity assertion.

The HTTP adapter expects the remote local Trace endpoint's current object
response. Changing that response to an array requires coordinated adapter,
endpoint, and test changes.

## Discovery in 0.3.0

Applications explicitly register their immediate dependencies. Discovery enumerates
those registrations recursively and reports paths, completeness, and branch issues.
Indirect destination Trace resolves a unique route only after complete discovery.
Explicit routed Trace can execute a chosen path without discovery.

Aspire service discovery resolves addresses; PingTrace discovery resolves diagnostic
aliases. There is no central registry, network scanning, or cached topology.
See [discovery and routing](../architecture/DISCOVERY-AND-ROUTING.md).

## Intended next capabilities

| Capability | Direction |
| --- | --- |
| Uniform Trace responses | Return collections consistently |
| Structured payload | Support an object payload |
| Trace helpers | Centralize result construction and completion |
| HTTP HEAD | Support HEAD on Ping in the ASP.NET Core adapter |
| Endpoint authorization | Allow anonymous Ping and protected Trace independently |
| Blazor viewer | Present results as a log, table, or dependency graph |

These capabilities are not available merely because they appear in examples
or design discussions.

## Open design decisions

### Further traversal controls

TraceAll already implements per-path cycle detection, sequential execution,
and shared-dependency visits per path. Default depth is eight connector hops;
validation accepts a maximum combined ancestor count and remaining depth of
sixteen. An absolute deadline crosses process boundaries, with each runtime
also applying a thirty-second cooperative cap.

Propagated exceptions stop traversal without returning accumulated results.
Failed diagnostic results allow traversal to continue. Separate limits on
total requests or results, structured partial failures, and graph relationships
remain possible future work. See
[recursive execution](../architecture/PINGTRACE.md#recursive-traceall).

### Destination routing

Implemented routing uses arrays of registered connector aliases. Local and direct
matches take precedence; otherwise the runtime discovers a unique indirect route.
Multiple matches, incomplete discovery, and missing destinations are distinct errors.
Automatic rerouting and route caching remain unimplemented.

### Graph representation

A flat result collection does not encode a branching topology.

Future visualization needs a way to associate observations with paths or
parent-child relationships. That does not necessarily require restoring
permanent application IDs.

### Health Checks integration

PingTrace should complement health evaluation and follow familiar
registration conventions.

Whether to introduce an optional adapter or inherit from `IHealthCheck`
remains undecided. No integration is currently implemented.

### Database provider expansion

The implemented EF Core adapter reads SQL Server's server instance name,
current login name, and database name. The application measures timing.

Support for additional providers and configurable diagnostic queries remains
future design work. The existing generic DbContext constraint does not imply
provider-independent SQL.

### Custom registration

A generic `AddPingTrace<T>()` overload has been discussed but is not
implemented. Applications can currently register `IPingTrace` directly
before calling `AddPingTrace()`.

## Outside the initial scope

- Scheduling monitoring calls.
- Restarting or scaling applications.
- Defining universal health thresholds.
- Running business transactions as synthetic checks.
- Persisting diagnostic history.
- Providing a distributed tracing backend.

Applications and monitoring systems may build these capabilities around
PingTrace.
