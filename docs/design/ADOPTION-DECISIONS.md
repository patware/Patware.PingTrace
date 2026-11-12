# Implemented design decisions

This document records implemented choices, their justification, and their
consequences.

It distinguishes deliberate design decisions from current implementation
details that remain candidates for change.

See [capability boundaries](CAPABILITY-BOUNDARIES.md) for planned features
and unresolved questions.

## 1. Separate contracts, runtime, and adapters

**Decision:** Maintain separate libraries for Core, Runtime, incoming
ASP.NET Core endpoints, and outgoing HTTP communication.

**Justification:** Diagnostic execution is useful across hosting technologies.
HTTP routing and client configuration should not become requirements of
the core contracts.

**Consequences:**

- Core defines operations, connector contracts, and results.
- Runtime supplies default execution and registration.
- AspNetCore maps incoming requests through `IPingTrace`.
- The HTTP adapter supplies dependency communication and registration.
- Adding another adapter should not require adding its transport dependencies
  to Core or Runtime.

The HTTP adapter references Runtime because its extension methods operate
on `PingTraceBuilder`. Runtime does not reference the adapter.

## 2. Provide parameterless registration for a leaf application

**Decision:** Support:

```csharp
builder.Services.AddPingTrace();
app.MapPingTrace();
```

**Justification:** An application should be able to expose useful local
diagnostics without configuring dependencies or writing an implementation.

**Consequences:**

- The default runtime has no required application-options callback.
- Its application name comes from the managed entry assembly.
- The host still owns endpoint access and other hosting configuration.
- Entry-assembly inference can be unsuitable for specialized hosts or tests.

The current `PingTraceOptions` type is not consumed by registration or
the default runtime.

## 3. Remove diagnostic IDs

**Decision:** Remove application and destination IDs from the diagnostic
model and registration requirements.

**Justification:** The implemented operations do not require persistent GUIDs.
Requiring them adds configuration and model complexity without a current
consumer.

**Consequences:**

- Destination aliases identify configured connectors.
- Application names identify reported applications.
- Machine names provide additional execution context.
- The current model does not provide unique process identification or
  graph-edge correlation.

A future graph feature may require explicit relationship information.
That need should be addressed directly rather than assuming a permanent
application GUID solves it.

## 4. Discover registered destinations on demand

**Decision (0.3.0):** Add recursive discovery and explicit routed tracing while
keeping immediate dependency registration application-owned. This supersedes
the initial exclusion of discovery.

**Justification:** A frontend can diagnose a dependency owned by an API without
knowing that API's transitive topology at compile time. Discovery avoids running
all diagnostic probes simply to learn a route.

**Consequences:** Discovery is uncached and requires complete information before
automatic unique-route selection. Ambiguity and incomplete branches are reported
explicitly. Aspire still resolves service addresses; PingTrace discovers aliases
and routes. Expected values and monitoring policy remain application-owned.

## 5. Register dependencies through a builder

**Decision:** The optional `AddPingTrace` callback receives
`PingTraceBuilder`.

```csharp
builder.Services.AddPingTrace(pingTrace =>
{
    pingTrace.AddHttpDestination("ApiService", client =>
    {
        client.BaseAddress = new Uri("https://api.example.com/");
    });
});
```

**Justification:** Adapter registration needs access to the application's
service collection and transport-specific configuration.

A builder supports adapter extension methods without requiring Runtime
to know every adapter type.

**Consequences:**

- The callback executes during registration.
- The builder exposes `IServiceCollection`.
- The callback is not a `PingTraceOptions` configuration callback.
- Each callback executes even when the default runtime is already registered.
- Repeating dependency registrations can produce duplicate-name errors.

## 6. Separate dependency invocation from application orchestration

**Decision:** Represent dependencies with `IPingTraceConnector`.

**Justification:** Applications should be able to add ordinary dependencies
without reimplementing local results, destination selection, and result
combination.

**Consequences:**

- A connector owns protocol, addressing, credentials, and response handling.
- The runtime creates the caller's local result.
- The runtime prepends that result to the connector's results.
- Connectors must not add a duplicate caller result.
- A custom `IPingTrace` remains available for different application behavior.

Connector result collections do not automatically describe or trigger
recursive traversal.

## 7. Select destinations by explicit, case-insensitive names

**Decision:** Use an ordinal, case-insensitive destination lookup.

**Justification:** Destination aliases should be convenient for callers
while remaining unambiguous.

**Consequences:**

- Names differing only by case cannot identify separate dependencies.
- A destination cannot use the local application's name.
- Empty names are rejected.
- Validation occurs when the default runtime is constructed.
- Names are not automatically trimmed.

The local application name is also accepted as a destination, producing
a one-element collection containing the local trace.

As of 0.3.0, the owner's alias replaces the immediate HTTP result's name.
Descendant names remain owned by the applications registering them.

## 8. Use scoped runtime and connector registrations

**Decision:** Register the default runtime and built-in HTTP connectors
with scoped lifetimes.

**Justification:** Scoped registration permits connectors to participate
in application scopes and use compatible scoped dependencies.

**Consequences:**

- An ASP.NET Core request resolves the runtime through its request services.
- Other consumers must resolve scoped services within an appropriate scope.
- Connector constructors run when the runtime's dependencies are resolved.
- Constructors should avoid network operations and expensive work.

The current runtime constructs a destination lookup for each runtime instance.

## 9. Preserve an application-provided implementation

**Decision:** Register the default `IPingTrace` implementation using
`TryAddScoped`.

**Justification:** An application should be able to supply diagnostic behavior
without the convenience registration replacing it.

**Consequences:**

- Registering `IPingTrace` before `AddPingTrace()` preserves that implementation.
- Repeated parameterless calls do not add another default implementation.
- A custom implementation owns its own connector usage and orchestration.

The generic `AddPingTrace<T>()` syntax discussed during design is not
implemented. Ordinary dependency injection currently provides this
extension point.

## 10. Keep Ping and local Trace dependency-free

**Decision:** Invoke connectors only for named dependency traces.

**Justification:** Frequent probes need predictable cost. Local Trace also
provides a baseline for isolating dependency-related delays.

**Consequences:**

- Ping returns `Pong`.
- Local Trace constructs and completes a local result.
- Registering a dependency does not make local operations probe it.
- A named request invokes only its selected connector.

This concerns execution of connector tracing methods. Connector construction
still occurs during runtime resolution.

## 11. Use factory-managed named HTTP clients

**Decision:** Give each HTTP destination a named client created through
`IHttpClientFactory`.

**Justification:** Different destinations need independent addresses and
client configuration while participating in the host's HTTP infrastructure.

**Consequences:**

- Multiple destinations can share the connector implementation.
- The adapter creates its client when tracing executes.
- `AddHttpDestination()` returns `IHttpClientBuilder` for further configuration.
- Host-configured client defaults can supply service discovery and resilience.
- Client-specific settings from a business client are not automatically copied.

The current internal client name is `PingTrace.Http:<destination name>`.
An overload accepting an existing named-client name is not implemented.

## 12. Keep direct destination Trace local at the remote application

**Decision:** For an immediate HTTP destination, the connector requests the configured local Trace
endpoint, defaulting to `/pingtrace/Trace`.

**Justification:** A direct match retains its limited execution cost and existing
response contract. TraceAll visits every branch; indirect named Trace uses
discovery and explicit forwarding to execute only its resolved path.

**Consequences:**

- The adapter expects one JSON trace object.
- It wraps that object in a collection for the runtime.
- The caller's destination alias is not forwarded to the remote application.
- Remote dependencies are not automatically traced.
- TraceAll uses a separate recursive endpoint and response contract.

A null response or a result with an empty name is rejected.

## 13. Report operational failures and preserve cancellation

**Decision (0.3.0):** Known operational failures become structured failed
`TraceResult` entries. Unexpected exceptions and cancellation propagate.

**Justification:** An unsuccessful diagnostic is useful evidence, not a reason
to hide the remaining dependencies. Status, classification, code, and message
explain failure while preserving collected metadata and payload.

**Consequences:** TraceAll continues after failed results while time remains.
Thrown exceptions stop traversal without returning accumulated results. Logging
retains handled exceptions; public results contain selected explanations. Routing
failures use `TraceRoutingException` and a separate structured HTTP error contract.
The endpoint retains its legacy `KeyNotFoundException` catch for custom implementations.

## 14. Keep authorization and monitoring policy with the host

**Decision:** Expose endpoints that participate in host conventions without
configuring an authentication scheme or health policy.

**Justification:** Applications differ in their access requirements,
expected observations, and recovery actions.

**Consequences:**

- The host supplies authentication and authorization.
- The monitor supplies schedules, thresholds, and actions.
- Reported runtime identity is diagnostic data, not caller authentication.
- Incoming access and outgoing credentials are configured separately.

The current mapping returns one group containing Ping, Trace, TraceAll, Discover, and TraceRoute endpoints.
Independent authorization for those endpoints remains planned work.

## 15. Organize tests around library responsibilities

**Decision:** Maintain separate Runtime, AspNetCore, HTTP adapter, and
Entity Framework adapter test projects.

**Justification:** The boundaries make the behavior under test and required
dependencies easier to identify.

**Consequences:**

- Runtime tests exercise orchestration and registration.
- HTTP adapter tests exercise configured clients and transport behavior.
- ASP.NET Core tests exercise endpoint behavior.
- The adapter-to-endpoint round-trip test currently remains in the
  AspNetCore test project.
- There is no dedicated Core or cross-library integration test project yet.
- EF adapter tests exercise result materialization, query failure propagation,
  cancellation, and deferred context resolution using intercepted commands.

Tests use in-memory substitutes where practical. They do not replace
deployment verification.

## 16. Make recursive traversal an explicit operation

**Decision:** Expose TraceAll separately from local and named destination Trace.
Use an optional recursive connector interface and pass traversal state across
process boundaries.

**Justification:** Explicit recursion makes its greater execution cost visible
to callers. Existing connectors remain useful as leaves. Sequential traversal
supports connectors sharing a scoped DbContext.

**Consequences:**

- Runtime traversal is sequential and ordered by destination name.
- Shared dependencies are visited through each path.
- Singleton node identities support cycle detection across request scopes.
- Depth limits and cooperative deadlines bound traversal.
- HTTP recursion uses POST to carry traversal state.
- Failures stop traversal without returning partial results.
- Results remain a flat collection.

See [recursive execution](../architecture/PINGTRACE.md#recursive-traceall) for
the current defaults and limitations.

## Current implementation details that remain open to change

The following describe the code today. They are not commitments to preserve
these shapes indefinitely.

| Detail | Current behavior | Proposed direction |
| --- | --- | --- |
| Local Trace result | One object | Consistent collection response |
| Destination Trace result | Collection | Preserve a useful representation as traversal evolves |
| Payload | String; application assembly version or connector-specific diagnostics | Structured payload |
| Duration | Difference between `DateTime.Now` timestamps | Monotonic duration measurement |
| Result relationships | Flat ordered collection | Explicit paths or relationships if required by graph features |
| HTTP methods | GET Ping/Trace/TraceAll/Discover; POST TraceAll/Discover/TraceRoute | HEAD support on Ping |
| Authorization mapping | One shared group | Independent Ping and diagnostic configuration |

Changing the local response shape requires coordinated changes to Core,
Runtime, AspNetCore, the HTTP adapter, tests, and documentation.

## Decisions still to make

The following remain design work:

- Additional total request or result-count limits for recursive traversal.
- Multi-hop destination routing.
- Partial-failure representation.
- Trace helper APIs.
- Typed custom registration and application-default overrides.
- Additional database providers and configurable diagnostic queries.
- Health Checks integration.
- Blazor visualization and path timing.

These proposals must not appear as implemented capabilities in runnable
examples.

## Related documentation

- [Architectural principles](../architecture/ARCHITECTURAL-PRINCIPLES.md)
- [Architecture](../architecture/ARCHITECTURE.md)
- [Extension contracts](../architecture/PLUGIN-ARCHITECTURE.md)
- [Capability boundaries](CAPABILITY-BOUNDARIES.md)
