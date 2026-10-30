# Changelog

User-visible changes are recorded here. `VERSION` is the current build version;
published releases are identified by package and release date below.

## Unreleased

### Changed

- Update versions of NuGet package reference

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
