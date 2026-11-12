# Changelog

User-visible changes are recorded here. `VERSION` is the current build version;
published releases are identified by package and release date below.

## 0.3.0 - 2026.11.11

### Added

- Redis cache connector using an application-registered
  `IConnectionMultiplexer`, with deferred connection resolution and
  bounded waiting for the ping operation.
- Structured diagnostic outcomes on `TraceResult`: `Status`,
  `FailureKind`, `ErrorCode`, and `Message`.
- `TraceResult.Fail(...)` to record failed completion while preserving
  previously collected payload and identity metadata.
- Warning logs for locally generated connector failures, including
  original exceptions when available.
- Recursive destination discovery, separate from diagnostic execution.
  Discovery lists registered destinations and the connector aliases
  needed to reach them without executing leaf diagnostic probes.
- Discovery contracts: `IPingTraceDiscovery`,
  `IDiscoverablePingTraceConnector`, `DiscoveryRequest`,
  `DiscoveryResult`, `DiscoveredRoute`, and `DiscoveryIssue`.
- Discovery completeness reporting through `IsComplete` and branch
  issues. Reports distinguish complete discovery from branches that
  could not be explored or returned invalid responses.
- Explicit routed tracing through `IPingTraceRouter`,
  `IRoutedPingTraceConnector`, and `TraceRouteRequest`.
- Structured routing errors through `TraceRoutingError` and
  `TraceRoutingException`, with error codes, explanatory messages,
  candidate routes, and discovery issues where applicable.
- ASP.NET Core GET and POST Discover endpoints and a POST TraceRoute
  endpoint under the configured PingTrace route prefix.
- Configurable HTTP discovery and routed-trace endpoint paths.
- Unit-test coverage for Redis, structured outcomes, failure
  classification, logging, cancellation, and JSON compatibility.
- Regression coverage for owner-defined HTTP destination names,
  separate registrations of the same remote application, and
  preservation of descendant names and diagnostic evidence.

### Changed

- Named Trace can now reach indirect destinations. When a destination
  is neither the local application nor an immediate registration,
  the runtime discovers reachable destinations and executes the
  uniquely resolved route.
- Local application and immediate destination matches retain
  precedence and do not require discovery.
- Destination matching and route comparison are case-insensitive.
  Returned HTTP destination names preserve the owner's registered
  spelling.
- HTTP Trace, TraceAll, and routed Trace now name the immediate remote
  result using the owner's `AddHttpDestination(name: ...)` registration
  instead of the remote application's self-reported name.
  Descendant names remain those assigned by their respective owners.
- Known operational failures in Redis, Entity Framework, Microsoft
  Graph, and Active Directory connectors now return failed diagnostic
  results instead of aborting tracing.
- HTTP failures now use structured diagnostic fields. Missing HTTP base
  addresses and invalid diagnostic responses also return failed results.
- TraceAll continues visiting subsequent destinations after a connector
  returns a failed result, while its execution deadline permits.
- `TraceResult.Finish(...)` now explicitly marks success and clears
  previous failure details.
- PingTrace registration now includes logging services. Applications
  retain control of logging providers, filtering, and retention.
- The Aspire sample includes discovery and indirect-destination links
  demonstrating Web-to-API-to-dependency tracing.
- Updated connector documentation and NuGet dependencies while retaining
  the .NET 10 target framework.

### Discovery and routing behavior

- Discovery traverses participating connectors sequentially and records
  routes as ordered arrays of registered connector aliases.
- Discovery applies ancestor-based cycle detection, depth limits,
  deadlines, and cooperative cancellation. Reports are bounded to
  256 routes and 64 issues.
- Discovery defaults to eight connector hops. Validated traversal
  context supports a maximum of sixteen hops.
- Automatic indirect-destination resolution requires complete discovery
  and exactly one distinct matching route. It does not select the first
  matching branch when alternatives or unexplored branches remain.
- A complete discovery with no matching destination reports
  `Routing.DestinationNotFound`. Multiple distinct matching routes
  report `Routing.AmbiguousDestination`.
- Incomplete discovery prevents automatic route selection and reports
  `Routing.DiscoveryIncomplete`, unless multiple matches already
  establish ambiguity.
- Explicit routed tracing executes only the selected connector path.
  It does not probe sibling destinations or reroute after a diagnostic
  failure.
- Automatic discovery and route execution share a thirty-second
  deadline. Discovery time is excluded from diagnostic result timings
  but still consumes the overall request budget.
- ASP.NET Core maps structured routing failures to HTTP 404 for missing
  destinations or route segments, 409 for ambiguity or cycles, 503 for
  incomplete discovery, and 501 for unsupported forwarding.
- A Discover response can return HTTP 200 with `IsComplete` set to
  `false`; callers must inspect the report and its issues.

### Compatibility notes

- HTTP result naming is a behavior change. Consumers expecting the
  remote assembly or application name in `TraceResult.Name` must now
  use the destination alias registered by its immediate owner.
  The local root result retains its application name.
- Destination names are scoped to their owner and are not globally
  unique. Discovery route segments distinguish destinations reached
  through different branches.
- Unknown named destinations no longer necessarily produce an immediate
  NotFound response: they can trigger recursive discovery first.
- The default runtime reports destination-resolution failures through
  `TraceRoutingException`. In-process consumers previously catching
  only `KeyNotFoundException` should handle the structured exception.
- Routing errors are distinct from failed diagnostics. Routing errors
  explain why a path could not be resolved or executed; failed
  `TraceResult` entries describe diagnostic operations that ran.
- Discovery and routing are optional capabilities exposed through new
  interfaces. Existing `IPingTrace` implementations and leaf connectors
  are not required to implement them.
- Indirect tracing requires discovery and forwarding support along the
  selected path. Older peers can remain directly traceable without
  supporting these capabilities.
- Existing HTTP registration overloads remain available. Additional
  overloads allow explicit discovery and routed-trace paths.
- Connector failure explanations now appear in `Message` rather than
  `Payload`. Consumers should inspect `Status`, `FailureKind`, and
  `ErrorCode`; `Payload` remains diagnostic evidence.
- Results without an explicit status, including responses from older
  producers, default to `Unknown` and must not be interpreted as success.
- Each result's status describes its own operation, not the combined
  health of its dependencies. HTTP 200 does not imply that every
  diagnostic operation succeeded.
- Caller cancellation, traversal deadline exhaustion, and unexpected
  exceptions still propagate. Configuration and authentication failures
  are converted only where the connector explicitly handles them.

### Current limitations

- Discovery results are not cached. Each automatic resolution of an
  indirect destination performs discovery again.
- Discovery describes registered topology, not destination health.
  A discovered destination can fail when traced.
- Topology can change between discovery and execution. Routed tracing
  reports execution failures rather than automatically rediscovering
  or choosing another path.
- Trace results remain flat collections without explicit route fields.
  Repeated descendant names in different branches are not uniquely
  identified by `Name` alone.
- Cancellation remains cooperative and cannot forcibly interrupt an
  uncooperative connector.

## 0.2.0 - 2026.10.15

### Changed

- HTTP destination transport failures, client timeouts, and unsuccessful
  HTTP responses now produce diagnostic results instead of aborting tracing.
  TraceAll continues visiting subsequent destinations while its deadline permits.
- Consumers must inspect diagnostic results rather than treating the absence
  of an exception as evidence that every destination responded successfully.
- Caller cancellation and overall traversal deadline exhaustion still
  stop execution.

### Added

## 0.1.0 - 2026.10.13

### Added

- Initial NuGet release of the seven Patware.PingTrace packages for .NET 10.
- Application Ping, local Trace, and named-destination Trace.
- Recursive TraceAll with sequential traversal, cycle detection,
  depth limits, and cooperative cancellation.
- ASP.NET Core Ping, Trace, and GET/POST TraceAll endpoints.
- HTTP tracing of remote PingTrace applications.
- SQL Server diagnostics through Entity Framework Core.
- Microsoft Graph organization diagnostics.
- Active Directory diagnostics with an application-configured LDAP reader.
- Package READMEs, XML documentation, and symbol packages.

### Current limitations

- Local Trace returns an object; destination Trace and TraceAll return arrays.
- Dependency failures stop traversal without returning partial success.
- The Entity Framework adapter uses SQL Server-specific diagnostics.
- Synchronous LDAP operations cannot be interrupted by cooperative cancellation.
- Applications configure endpoint authentication and authorization.
