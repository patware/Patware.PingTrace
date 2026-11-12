# PingTrace

**Is it responding? Where is it running? What is it reaching?**

PingTrace provides application-level Ping and Trace diagnostics for .NET
applications.

Use Ping to confirm that application code responds. Use Trace to identify the
responding application, machine, execution identity, and selected dependencies.
Use the returned timing information to help investigate unexpected delays.

PingTrace complements health checks, deployment verification, and operational
monitoring.

## Start with two calls

In an ASP.NET Core application referencing `PingTrace.Runtime` and
`PingTrace.AspNetCore`:

```csharp
using PingTrace.AspNetCore;
using PingTrace.Runtime;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddPingTrace();

WebApplication app = builder.Build();

app.MapPingTrace();

app.Run();
```

This is sufficient for an application that needs local diagnostics without
dependency tracing.

| Request | Default behavior |
| --- | --- |
| `GET /pingtrace/Ping` | Returns `Pong` as plain text. |
| `GET /pingtrace/Trace` | Returns one local trace object. |
| `GET /pingtrace/Trace?destination=NAME` | Traces the named destination and returns a result array. |
| `GET /pingtrace/TraceAll` | Starts recursive traversal of registered destinations and returns an array. |
| `POST /pingtrace/TraceAll` | Continues traversal using the supplied JSON request context. |
| `GET /pingtrace/Discover` | Starts discovery; returns routes, completeness, and issues. |
| `POST /pingtrace/Discover` | Continues discovery with request context. |
| `POST /pingtrace/TraceRoute` | Executes an explicit route and returns a trace array. |

Destination tracing requires a registered connector unless the destination
matches the local application's name.

Trace responses contain potentially sensitive diagnostic information.
Applications control access through their hosting and authorization policies.

See the [execution model](architecture/PINGTRACE.md) for response shapes,
destination handling, timing, and failures.

## Trace an HTTP dependency

Also reference `PingTrace.Adapter.HttpClient` and replace the registration with:

```csharp
builder.Services.AddPingTrace(pingTrace =>
{
    pingTrace.AddHttpDestination("ApiService", client =>
    {
        client.BaseAddress = new Uri("https://api.example.com/");
    });
});
```

Add `using PingTrace.Adapter.HttpClient;` to the application.

Replace the example address with the remote application's address. The remote
application must expose a compatible local Trace endpoint.

Requesting `/pingtrace/Trace?destination=ApiService` returns the local result
followed by the remote result.

The HTTP adapter requests the remote application's local Trace. It does not
automatically traverse that application's dependencies.

See [distributed hosting](architecture/DISTRIBUTED-HOSTING.md) for the Aspire
sample and [connector architecture](architecture/PLUGIN-ARCHITECTURE.md) for
extension responsibilities.

## Choose the libraries you need

The current libraries target .NET 10.

| Library | Responsibility |
| --- | --- |
| `PingTrace.Core` | Shared interfaces and trace results. |
| `PingTrace.Runtime` | Default behavior, service registration, and dependency orchestration. |
| `PingTrace.AspNetCore` | HTTP endpoints backed by an `IPingTrace` implementation. |
| `PingTrace.Adapter.HttpClient` | Connectors that invoke remote HTTP Trace endpoints. |
| `PingTrace.Adapter.EntityFramework` | SQL Server diagnostics through a registered DbContext. |
| `PingTrace.Adapter.MicrosoftGraph` | Organization diagnostics through a registered GraphServiceClient. |
| `PingTrace.Adapter.ActiveDirectory` | Domain diagnostics through a registered directory reader. |
| `PingTrace.Adapter.RedisCache` | Redis diagnostics through a registered IConnectionMultiplexer. |

Applications supply their destination addresses, authentication requirements,
and authorization policies.

The Aspire sample includes a simulated Graph destination that requires no
Microsoft tenant. See the [simulation walkthrough](development/GETTING-STARTED.md#exercise-the-simulated-microsoft-graph-destination).

The sample also includes [simulated AD diagnostics](development/GETTING-STARTED.md#exercise-the-simulated-active-directory-destination)
using the real connector with a substituted reader, without a domain controller.

## Find your next step

| I want to… | Read |
| --- | --- |
| Understand Ping, Trace, and the intended monitoring scenarios | [Definitions](architecture/PINGTRACE-DEFINITIONS.md) |
| Understand library boundaries and request flow | [Architecture](architecture/ARCHITECTURE.md) |
| Understand responses, failures, and elapsed time | [Execution model](architecture/PINGTRACE.md) |
| Connect applications in the Aspire sample | [Distributed hosting](architecture/DISTRIBUTED-HOSTING.md) |
| Implement a connector or custom application behavior | [Connector architecture](architecture/PLUGIN-ARCHITECTURE.md) |
| Control access to diagnostic information | [Security](architecture/SECURITY.md) |
| Use PingTrace with monitoring and deployment checks | [Observability](architecture/OBSERVABILITY.md) |
| Understand the reasoning behind the design | [Architectural principles](architecture/ARCHITECTURAL-PRINCIPLES.md) and [implemented decisions](design/ADOPTION-DECISIONS.md) |
| Distinguish implemented features from future possibilities | [Capability boundaries](design/CAPABILITY-BOUNDARIES.md) |
| Build the libraries or run the sample | [Getting started](development/GETTING-STARTED.md) |
| Contribute code and tests | [Coding standards](development/CODING-STANDARDS.md) and [testing](development/TESTING.md) |

## Current scope

The implementation supports Ping, local Trace, explicitly named destination
Trace, and [recursive TraceAll](architecture/PINGTRACE.md#recursive-traceall).

Version 0.3.0 includes discovery of explicitly registered destinations and indirect
tracing. Graph visualization and built-in health-check integration remain future work.
See [discovery and routing](architecture/DISCOVERY-AND-ROUTING.md).

The documentation distinguishes existing behavior from proposed capabilities.
Design discussions do not imply that an API is available.

## API reference and documentation preview

DocFX metadata inputs include all `src/**/*.csproj` projects, targeting .NET 10
in Release configuration. Generate metadata before building the documentation site.

The Markdown guides describe the current implementation. See
[Getting started](development/GETTING-STARTED.md#preview-the-documentation)
for documentation preview instructions and the current API-generation
workflow.

## Project

Visit the [GitHub repository](https://github.com/patware/Patware.PingTrace)
for source code, package README files, contribution guidance, and issue
reporting.
