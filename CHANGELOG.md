# Changelog

User-visible changes are recorded here. `VERSION` is the current build version;
published releases are identified by package and release date below.

## Unreleased

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
- Unit-test coverage for Redis, structured outcomes, failure
  classification, logging, cancellation, and JSON compatibility.

### Changed

- Known operational failures in Redis, Entity Framework, Microsoft Graph,
  and Active Directory connectors now return failed diagnostic results
  instead of aborting tracing.
- HTTP failures now use structured diagnostic fields. Missing HTTP base
  addresses and invalid diagnostic responses also return failed results.
- TraceAll continues visiting subsequent destinations after a connector
  returns a failed result, while its execution deadline permits.
- `TraceResult.Finish(...)` now explicitly marks success and clears
  previous failure details.
- PingTrace registration now includes logging services. Applications
  retain control of logging providers, filtering, and retention.
- Updated connector documentation and NuGet dependencies while retaining
  the .NET 10 target framework.

### Compatibility notes

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

### Added

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
