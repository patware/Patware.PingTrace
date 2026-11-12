# Architectural principles

PingTrace exists to make deployment verification and application diagnostics
easy to adopt and useful during troubleshooting.

These principles guide implementation and review. They describe established
design choices and constraints for future work; they do not imply that every
planned capability is implemented.

See [capability boundaries](../design/CAPABILITY-BOUNDARIES.md) for current
feature status.

## 1. Make the simplest application simple to diagnose

An application without diagnostic dependencies should need only:

```csharp
builder.Services.AddPingTrace();
app.MapPingTrace();
```

Defaults reduce adoption work and make a useful local diagnostic available
before an application adds dependency tracing.

The current runtime derives its application name from the managed entry
assembly. It does not require an application ID.

Default inference has limits. Specialized hosts and test runners may supply
a different entry assembly than an application author expects. Future
override mechanisms should address concrete hosting needs without making
ordinary registration more complicated.

## 2. Keep contracts independent of transport

Core defines diagnostic operations, connector contracts, and results.

It should not acquire dependencies on ASP.NET Core routing, HTTP client
configuration, EF Core, or a presentation framework.

This allows the same diagnostic concepts to be used by web applications,
services, and other hosts.

Transport-specific behavior belongs in adapters. For example, HTTP HEAD
support belongs in the ASP.NET Core adapter rather than as a `Head()`
member on `IPingTrace`.

## 3. Separate registration, execution, and exposure

Each layer has a different responsibility:

| Layer | Responsibility |
| --- | --- |
| Registration | Declare implementations, dependencies, and configuration |
| Runtime | Execute diagnostic operations and combine results |
| Incoming adapter | Translate external requests into diagnostic calls |
| Outgoing connector | Communicate with a dependency |
| Presentation | Display observations to a person |

`AddPingTrace()` belongs to Runtime because service registration is useful
across hosting technologies.

`MapPingTrace()` belongs to AspNetCore because HTTP routes and endpoint
conventions are specific to that adapter.

The registration callback runs during startup. It must not perform
diagnostic requests or become a container for per-request state.

A future viewer should consume results without becoming a requirement for
applications that only expose diagnostic endpoints.

## 4. Keep Ping and local Trace local

Ping answers whether application code can return its predictable response.

Local Trace describes the responding application.

Neither operation should invoke dependency tracing. This keeps frequent
probes inexpensive and provides a local baseline when investigating a
dependency-related delay.

The distinction must remain clear even when many connectors are registered.

The current runtime constructs its connectors when it is resolved.
Connector constructors must therefore remain inexpensive and avoid
network operations.

## 5. Make additional work explicit

A named destination request invokes the selected connector.

It must not silently become a request to probe every registered dependency.

TraceAll is an explicit operation because recursive traversal
has a different cost and failure model.

Similarly, an on-demand investigation that probes several paths should
make that additional work visible rather than increasing routine polling
cost without the caller's knowledge.

The library does not choose monitoring intervals for the application.

## 6. Let each application declare its dependencies

Applications explicitly register the systems they know how to contact.

A frontend can register an API connector without maintaining a complete
description of the API's internal dependencies.

This keeps connection knowledge with the application that owns it.

Version 0.3.0 discovers explicitly registered destinations across participating
applications. Host address resolution, such as Aspire service discovery, remains
a separate responsibility.

Recursive tracing follows participating applications' configured dependencies.
It does not discover arbitrary network targets.

## 7. Prefer reusable connectors for ordinary integrations

An application should not need to replace `IPingTrace` merely because it
uses HTTP or another common communication mechanism.

A connector owns transport details:

- Addressing and protocol.
- Authentication and client configuration.
- Request execution.
- Response validation and translation.

The default runtime owns the local result, connector selection, and result
ordering.

A custom `IPingTrace` implementation remains an extension point when an
application needs different local behavior or orchestration.

Reusable adapters should integrate with the application's existing client
and dependency-injection facilities where practical.

## 8. Report observations accurately

A result should identify what the participating component actually reports.

The owner defines a destination's name. The HTTP connector assigns that alias
to the immediate remote result, preserving descendants' owner-defined names.
Machine, identity, payload, outcome, and timing remain remote diagnostic evidence.

Likewise, a connector must not present its own machine or runtime identity
as though those values came from a remote database or service.

A connection test establishes connectivity. It does not automatically
establish remote execution identity, business correctness, or health.

Monitoring policy interprets observations; the diagnostic model should
not imply evidence it does not contain.

## 9. Define timing boundaries honestly

Elapsed time must be described in terms of the operation being measured.

The current local result for a destination trace includes the awaited
connector operation. It does not measure the entire incoming HTTP request.

Parent and child durations overlap. They cannot simply be added, and their
difference is not a pure network-latency measurement.

The current implementation uses wall-clock timestamps. A future monotonic
timer would improve duration measurement, but documentation must not claim
that improvement before it exists.

Visualization should preserve these distinctions rather than label every
duration as network latency.

## 10. Preserve failures and cancellation

A failed dependency call must not quietly become an empty successful
observation.

The current runtime propagates connector exceptions and cancellation.
Handled failures return failed diagnostic results and allow traversal to continue.
A propagated exception stops traversal without returning accumulated results.

Custom connectors should pass cancellation to operations that support it
and avoid masking cancellation as success.

Structured outcomes distinguish successful and failed observations. Discovery
separately reports incompleteness; interrupted TraceAll does not return a partial report.

Recursive tracing uses path-local cycle detection, depth limits, and cooperative
deadlines. Shared dependencies are visited per path. There is no separate limit
on total requests or results, so hosts must choose probe frequency appropriately.

## 11. Keep health policy with the application and monitor

PingTrace supplies responses and observations.

The application or monitoring system decides:

- Which observations are expected.
- Which durations are acceptable.
- Whether a failure means degraded or unhealthy.
- Whether to notify, restart, scale, or change routing.

This allows PingTrace to complement health checks and hosting probes
without imposing one operational policy on every application.

Any future Health Checks integration must define how it translates
diagnostic observations into health classifications.

## 12. Keep access control with the host

The host chooses who can access diagnostics and which credentials outgoing
connectors use.

PingTrace should work with the host's ordinary authorization and HTTP client
configuration rather than introduce a parallel security framework.

Ping is intended to support anonymous monitoring. Trace exposes richer
information and may require restricted access.

The current mapping returns one group containing both endpoints.
Independent authorization for Ping and Trace remains a planned API
improvement.

Reported runtime identity, incoming caller identity, and outgoing
credentials must remain distinct concepts.

## 13. Add model complexity when a capability requires it

Diagnostic IDs were removed because the current behavior did not require
them.

Do not add fields, abstractions, or configuration solely to anticipate
unspecified future features.

At the same time, recognize when a concrete feature needs additional
structure. A branching graph cannot be reconstructed reliably from names
and array order alone.

Future graph support should introduce the path or relationship information
it needs, with a documented purpose for each addition.

## 14. Evolve contracts across producers and consumers together

HTTP endpoints produce results that adapters and monitoring tools consume.

A change to a response shape therefore affects more than one library.

For example, the current local endpoint returns one object and the HTTP
adapter deserializes one object. Changing local Trace to return a collection
requires coordinated changes to the contract, endpoint, adapter, tests,
and documentation.

Treat an incompatible response change as a compatibility decision rather
than a formatting cleanup.

## 15. Test public behavior at the appropriate boundary

Runtime tests should verify local execution, connector selection, ordering,
failures, and registration behavior.

Adapter tests should verify transport requests, configuration, response
handling, and cancellation.

Endpoint tests should verify routing, response formats, and HTTP behavior.

Cross-library tests should verify that actual producers and consumers
agree on their contract.

Prefer isolated handlers and in-memory hosts for deterministic tests.
Use deployed-system checks for questions those tests cannot answer, such
as real network paths, authentication configuration, or IIS deployment.

Tests should not depend on arbitrary sleeps or machine-speed thresholds.

## 16. Keep documentation aligned with implementation

Runnable examples must use APIs present in the repository.

Separate implemented behavior, planned capabilities, and unresolved
decisions. A discussed API is not an available feature.

Document public members with XML comments and include useful design
justification in remarks.

When behavior changes, update the corresponding examples, tests, and
capability descriptions together.

## Related documentation

- [Architecture](ARCHITECTURE.md)
- [Execution model](PINGTRACE.md)
- [Extension contracts](PLUGIN-ARCHITECTURE.md)
- [Monitoring and observability](OBSERVABILITY.md)
- [Security](SECURITY.md)
- [Implemented design decisions](../design/ADOPTION-DECISIONS.md)
- [Capability boundaries](../design/CAPABILITY-BOUNDARIES.md)
