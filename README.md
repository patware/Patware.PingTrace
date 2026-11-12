# PingTrace

**Is it responding? Where is it running? What is it reaching?**

PingTrace brings application-level Ping and Trace operations to .NET
applications. Use it to verify deployments, identify unexpected destinations,
and investigate delays between application components.

A lightweight Ping confirms that an application endpoint responds.
A Trace reports the application name, machine, runtime identity, timing,
and a diagnostic payload.

## Install

PingTrace 0.3.0 targets .NET 10.

For an ASP.NET Core application:

```shell
dotnet add package Patware.PingTrace.Runtime --version 0.3.0
dotnet add package Patware.PingTrace.AspNetCore --version 0.3.0
```

To trace another PingTrace application over HTTP:

```shell
dotnet add package Patware.PingTrace.Adapter.HttpClient --version 0.3.0
```

NuGet package IDs use the `Patware.PingTrace` prefix.
C# namespaces remain `PingTrace.*`.

The 0.x series is under active development. Review release notes for
compatibility changes before upgrading.

## Start with two calls

In an ASP.NET Core application referencing `PingTrace.Runtime` and
`PingTrace.AspNetCore`:

```csharp
using PingTrace.AspNetCore;
using PingTrace.Runtime;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPingTrace();

var app = builder.Build();

app.MapPingTrace();

app.Run();
```

The libraries currently target .NET 10.

| Request | Response |
| --- | --- |
| `GET /pingtrace/Ping` | Plain text: `Pong` |
| `GET /pingtrace/Trace` | One JSON object describing the local application |
| `GET /pingtrace/Trace?destination=NAME` | A JSON array containing the local result and results from the selected destination |
| `GET /pingtrace/TraceAll` | JSON array containing the local result followed by dependency results |
| `POST /pingtrace/TraceAll` | The same recursive result array |
| `GET /pingtrace/Discover` | Starts discovery; returns routes, completeness, and issues. |
| `POST /pingtrace/Discover` | Continues discovery with request context. |
| `POST /pingtrace/TraceRoute` | Executes an explicit route and returns a trace array. |

The default application name comes from the managed host's entry assembly.
No application ID or configuration callback is required.

Local Trace reports the managed entry assembly's version, falling back to
`Pong` when unavailable. Ping continues to return `Pong`.

Ping and local Trace do not invoke dependencies.

## Add an HTTP dependency

Reference `PingTrace.Adapter.HttpClient` and register a destination:

```csharp
using PingTrace.Adapter.HttpClient;
using PingTrace.AspNetCore;
using PingTrace.Runtime;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPingTrace(pingTrace =>
{
    pingTrace.AddHttpDestination("ApiService", client =>
    {
        client.BaseAddress = new Uri("https://api.example.com/");
    });
});

var app = builder.Build();

app.MapPingTrace();

app.Run();
```

Replace the example address with your API's address. The API must also
register and map PingTrace.

Request:

```text
GET /pingtrace/Trace?destination=ApiService
```

The frontend invokes the API's `/pingtrace/Trace` endpoint and returns:

```text
Frontend result
API result
```

`ApiService` is the owner's destination alias. The HTTP connector assigns it
to the first remote result; descendant names remain those assigned by their owners.

For direct destination Trace, the built-in HTTP adapter requests a local trace from the remote application.
It does not automatically trace that application's dependencies.

The registration callback receives a `PingTraceBuilder` for registering
adapters. It is not an application-options callback.

## Add a SQL Server dependency

Reference `PingTrace.Adapter.EntityFramework` and register a destination for
an existing SQL Server DbContext:

```csharp
using PingTrace.Adapter.EntityFramework;

builder.Services.AddPingTrace(pingTrace =>
{
    pingTrace.AddDbContextDestination<ApiServiceDbContext>("ApiServiceDb");
});
```

Register and configure `ApiServiceDbContext` separately with `AddDbContext`
and `UseSqlServer`.

Request the API service's:

```text
/pingtrace/Trace?destination=ApiServiceDb
```

The response contains the application's result followed by the database's
server instance name, current login name, and database name.

The current adapter uses SQL Server-specific SQL. It does not validate
application tables or migrations.

See the [Entity Framework adapter guide](src/PingTrace.Adapter.EntityFramework/README.md)
for configuration, result fields, lifetime, and test boundaries.

## Add a Microsoft Graph dependency

Reference `PingTrace.Adapter.MicrosoftGraph` and register the application's
configured `GraphServiceClient` separately. Add a destination using
`AddGraphOrganizationDestination("MicrosoftGraph", "tenant:...; app:...")`
inside the PingTrace registration callback.

The adapter reads organization metadata and reports the configured API host,
identity label, tenant ID, display name, and verified domain names. It does not
establish access to Teams resources. See the
[Microsoft Graph adapter guide](src/PingTrace.Adapter.MicrosoftGraph/README.md)
for registration, identity semantics, and test boundaries.

## Add an Active Directory dependency

Reference `PingTrace.Adapter.ActiveDirectory` and register a directory reader.
`AddActiveDirectoryDestination<TReader>("ActiveDirectory", @"CONTOSO\application-account")`
adds the destination inside the PingTrace registration callback. The included
LDAP reader binds and reads RootDSE and the domain object through an
application-configured connection.

See the [Active Directory adapter guide](src/PingTrace.Adapter.ActiveDirectory/README.md)
for registration, result fields, connection ownership, and the synchronous LDAP
cancellation limitation. The sample uses a simulated reader with the same connector.

## Trace all registered dependencies

Request the application's:

```http
GET /pingtrace/TraceAll
```

TraceAll starts a local trace and sequentially visits every registered
destination. HTTP destinations continue through the remote application's
TraceAll endpoint; database destinations perform their existing diagnostics.

For the Aspire sample, calling the frontend returns results for:

```text
AspireAppSample.Web
RedisCache
WeatherApiClient
ActiveDirectory
ApiServiceDb
MicrosoftGraph
```

The default traversal permits eight connector hops and uses a thirty-second
deadline. The HTTP adapter returns diagnostic entries for transport failures,
unsuccessful HTTP responses, and HttpClient timeouts. TraceAll continues
visiting subsequent destinations while its deadline permits.

Exceptions propagated by connectors, detected cycles, exhausted depth,
caller cancellation, and overall deadline exhaustion stop traversal
without returning the accumulated results.

Shared dependencies are visited through each path. Results remain a flat
array, and parent durations include descendant execution.

See [recursive execution](docs/architecture/PINGTRACE.md#recursive-traceall)
for request options, limits, timing, and failure behavior.

## Try the Aspire sample

The repository includes a Blazor frontend and an API service.

The frontend registers its API dependency using the same service address
as its weather client:

```csharp
builder.Services.AddPingTrace(pingTrace =>
{
    pingTrace.AddHttpDestination(nameof(WeatherApiClient), client =>
    {
        client.BaseAddress = apiServiceUri;
    });
});
```

With the sample running, open the frontend's:

```text
/pingtrace/Trace?destination=WeatherApiClient
```

The sample configures Aspire service discovery for its HTTP clients.
Its `https+http://apiservice` address relies on that configuration.

See [development setup](docs/development/GETTING-STARTED.md).

The API also registers a simulated Microsoft Graph destination using the real
Graph SDK and adapter. It requires no Microsoft account or tenant. Open the
API service's `/pingtrace/Trace?destination=MicrosoftGraph` to exercise it.
See the [simulation walkthrough](docs/development/GETTING-STARTED.md#exercise-the-simulated-microsoft-graph-destination)
for success, failure, and delay scenarios.

The API's `/pingtrace/Trace?destination=ActiveDirectory` exercises a simulated
directory reader without a domain controller. See the
[AD simulation walkthrough](docs/development/GETTING-STARTED.md#exercise-the-simulated-active-directory-destination).

## Diagnose deployments

PingTrace helps answer practical questions:

- Is application code receiving the request?
- Which application and machine responded?
- Which runtime identity does the process report?
- Does the configured dependency point to the expected system?
- Does an unexpected delay appear when a dependency is involved?

Static resources can complement these checks. An application-supplied
`ping.html` can help verify static-file delivery; an image can help verify
delivery with the expected content type. Application Ping then exercises
the application endpoint.

Each check provides evidence about the request path it exercises. Proxies,
caching, and the identity used for file access affect what that evidence
establishes.

Classic ASP pages, Web Forms pages, database procedures, and gRPC operations
are other ways to apply the same diagnostic approach. They are not all
provided as adapters by this repository.

## Understand the timing

For a destination trace, the local result includes the time spent awaiting
the dependency. Parent and child durations overlap.

Elapsed time is not a direct measurement of network latency. Authentication,
connection setup, serialization, retries, and remote processing can
contribute to it.

Compare local and destination traces to narrow the investigation.
For example, a fast backend local trace and a slow backend-to-database
diagnostic call point toward work associated with that dependency call.

## Fit the probe to its purpose

These intervals illustrate the intended usage model. PingTrace does not
schedule probes.

| Level | Example interval | Purpose | Current position |
| --- | --- | --- | --- |
| A | Every second | Minimal HTTP response | Explicit HEAD support is planned for the HTTP adapter. |
| B | Every 10 seconds | Confirm application response | Ping is implemented. |
| C | Every minute | Verify dependency connectivity | Named destination Trace and recursive TraceAll are implemented. |
| D | Every hour | Exercise representative business behavior safely | Application-owned synthetic checks are outside the initial scope. |

Choose intervals according to the cost of the participating systems.

PingTrace complements .NET Health Checks, Docker HEALTHCHECK, and Kubernetes
probes. Monitoring systems and applications own health policy, thresholds,
notifications, and recovery actions.

## Libraries

| Library | Responsibility |
| --- | --- |
| `PingTrace.Core` | Contracts and trace results |
| `PingTrace.Runtime` | Default execution and dependency-injection registration |
| `PingTrace.AspNetCore` | HTTP endpoint mapping |
| `PingTrace.Adapter.HttpClient` | Invocation of remote HTTP Trace endpoints |
| `PingTrace.Adapter.EntityFramework` | SQL Server diagnostics through a registered DbContext |
| `PingTrace.Adapter.MicrosoftGraph` | Organization diagnostics through a registered GraphServiceClient |
| `PingTrace.Adapter.ActiveDirectory` | Domain diagnostics through a registered directory reader |
| `PingTrace.Adapter.RedisCache` | Redis ping diagnostics through a registered IConnectionMultiplexer |

Registration belongs to Runtime so non-HTTP hosts can use it. HTTP routing
belongs to AspNetCore. Outgoing communication belongs to adapters.

## Endpoint access

Trace responses expose machine names, runtime identities, and payloads.
The host controls authentication and authorization.

The current mapping returns a route group:

```csharp
app.MapPingTrace().RequireAuthorization();
```

With authentication and authorization configured by the host, this protects
Ping, Trace, TraceAll, Discover, and TraceRoute endpoints. Separate anonymous Ping and protected Trace mapping
is a future API improvement.

## Current boundaries and direction

The current implementation supports Ping, local Trace, named destination Trace,
recursive TraceAll, HTTP dependencies, SQL Server diagnostics through EF Core,
Microsoft Graph organization diagnostics, and Active Directory domain diagnostics.

Version 0.3.0 adds destination discovery and routed tracing. See the
[discovery and routing guide](docs/architecture/DISCOVERY-AND-ROUTING.md).

Further work includes:

- A consistent collection response for all Trace operations.
- Structured payloads.
- Helpers for constructing local and composite traces.
- Explicit HTTP HEAD support on Ping.
- Optional Health Checks integration.
- A Blazor viewer for diagnostic results and dependency paths.

On-demand path probes may support richer latency investigations, but they
cost more than a single routine probe.

See [capability boundaries](docs/design/CAPABILITY-BOUNDARIES.md) for the
distinction between implemented behavior and planned work.

## Documentation

- [Documentation home](docs/index.md)
- [Definitions](docs/architecture/PINGTRACE-DEFINITIONS.md)
- [Architecture](docs/architecture/ARCHITECTURE.md)
- [Testing](tests/README.md)
- [Contributing](CONTRIBUTING.md)
- [License](LICENSE)

## Discovery and indirect destinations in 0.3.0

Request `GET /pingtrace/Discover` to list registered destination routes without
executing leaf diagnostic probes. From the Aspire frontend,
`GET /pingtrace/Trace?destination=ActiveDirectory` discovers and executes
`WeatherApiClient -> ActiveDirectory`. Direct registrations retain precedence.

Automatic resolution requires complete discovery and a unique route. Routing
errors return structured JSON; diagnostic failures remain `TraceResult` entries.
See [discovery and routing](docs/architecture/DISCOVERY-AND-ROUTING.md) for explicit
POST routes, error codes, deadlines, and compatibility.

## Add a Redis dependency

Reference `Patware.PingTrace.Adapter.RedisCache` and register an
`IConnectionMultiplexer` separately. The project and namespace retain the
spelling `Adapter`.

```csharp
using PingTrace.Adapter.RedisCache;

builder.Services.AddPingTrace(pingTrace =>
{
    pingTrace.AddRedisCacheDestination("RedisCache");
});
```

The connector resolves the existing connection when traced, returns `PONG`
on success, and does not own or dispose the connection. See the
[Redis guide](src/PingTrace.Adapter.RedisCache/README.md) for failure handling
and timeout boundaries.
