# Security and host responsibilities

PingTrace supplies diagnostic operations. The host controls who can invoke
them, which systems connectors can contact, and how diagnostic information
is handled.

The current library does not configure authentication, tenant isolation,
credential storage, or authorization policies.

## Information returned

The default operations expose different amounts of information:

| Operation | Information returned |
| --- | --- |
| Ping | The fixed string `Pong` |
| Local Trace | Application name, machine name, runtime identity, timestamps, elapsed time, and payload |
| Destination Trace | Local observations followed by dependency observations |
| TraceAll | Local observations followed by recursive dependency results |

Ping is intended to support inexpensive anonymous monitoring.

Trace can reveal infrastructure and execution details. Access should reflect
the audience permitted to inspect those details.

A successful frontend trace can include information returned by a backend.
The frontend therefore exposes the combined result to its caller, not just
its own observations.

## Current endpoint authorization

`MapPingTrace()` maps Ping, Trace, TraceAll, Discover, and TraceRoute endpoints into one route group.

It does not add `RequireAuthorization()` or `AllowAnonymous()` metadata.
Access therefore depends on the host's policies and surrounding infrastructure.

To protect the group using a policy already configured by the application:

```csharp
app.MapPingTrace()
    .RequireAuthorization("PingTraceReaders");
```

This protects all mapped PingTrace endpoints.

The host must register the named policy and configure a suitable
authentication scheme and authorization pipeline. The mapping call does
not perform those steps.

Calling `RequireAuthorization()` without a policy name uses the host's
default authorization policy.

An endpoint without explicit authorization metadata can still be protected
by a host fallback policy. Conversely, applying `AllowAnonymous()` to the
whole PingTrace group bypasses endpoint authorization for all mapped operations.

See [ASP.NET Core authorization policies](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-10.0)
for the framework's policy behavior.

### Anonymous Ping and protected Trace

The intended API should allow anonymous Ping and protected Trace
independently.

The current convenience mapping does not expose separate Ping and Trace
builders. Its returned builder represents the entire group.

Until that API exists, separate access rules require host-owned endpoint
mapping or another deliberate host-level policy arrangement.

Do not assume that applying authorization to `MapPingTrace()` protects
only Trace.

## Sample behavior

The Aspire sample maps PingTrace in both the frontend and API without
calling `RequireAuthorization()`.

Those mappings are not restricted to Development by the sample code.

The sample demonstrates communication and diagnostics. It does not
demonstrate a configured diagnostic-reader authorization policy.

The environment check used by the sample's ordinary health-check mapping
does not apply to its separate PingTrace mapping.

## Incoming and outgoing identities

Three identities may be involved in a destination trace:

| Identity | Purpose |
| --- | --- |
| Incoming caller | The browser, monitoring agent, or application invoking the endpoint |
| Reported runtime identity | The environment domain and user name reported by a participating process |
| Outgoing credential | The identity or credential used to contact a dependency |

These are not necessarily the same.

For example, a frontend process can report a Windows service account while
its HTTP connector authenticates to the API using an application token.

The current `TraceResult.Identity` is diagnostic data. It is not proof of
the caller's identity or the credential used for a remote connection.

A remote result is information reported by that remote system. PingTrace
does not independently attest its machine name or identity.

## Outgoing authentication

The HTTP adapter uses a named client created by `IHttpClientFactory`.

The application configures the client's authentication through its normal
HTTP client configuration and handlers.

`AddHttpDestination()` returns `IHttpClientBuilder` so the host can extend
that configuration.

The adapter does not automatically forward the incoming caller's
authorization header or impersonate that caller.

It also does not automatically reuse credentials or handlers configured
for another business client.

If diagnostic traffic is intended to verify the same access path as
business traffic, align the configurations deliberately.

A diagnostic call using a separate credential establishes only that the
diagnostic credential can access the remote endpoint.

## Destination configuration

HTTP callers supply a destination name, not an arbitrary remote address.

The default runtime resolves that name against local and immediate registrations,
then discovers indirect registered routes if needed. Resolution failures are
reported explicitly; discovery does not permit arbitrary outbound addresses.

The HTTP adapter's base address and trace path are configured by the
application during registration.

This keeps endpoint callers from directly choosing an arbitrary outbound
URL through the destination parameter. It does not replace control of the
application's configuration, HTTP handlers, or network access.

The adapter does not implement a separate outbound host allowlist or
redirect policy. Those controls belong to the configured client and host.

Custom connectors should preserve the distinction between configured
destination aliases and caller-supplied input.

Destination names are routing aliases, not secrets or authorization checks.

## Authorization granularity

The current runtime has no per-destination authorization check.

A caller authorized to invoke the Trace endpoint can request any destination
that the selected runtime exposes, subject to any additional host controls.

The library also has no built-in tenant boundary. A multitenant application
must ensure that diagnostic access and connector configuration follow its
own isolation rules.

A destination alias alone does not establish permission to inspect the
underlying system.

## Payloads

The current payload is a string. Custom implementations and remote systems
can supply its contents.

Keep payloads focused on information needed to diagnose the application.
Avoid returning credentials, access tokens, connection strings containing
secrets, or unrelated customer data.

The runtime does not redact dependency results before returning them.

If a host logs or stores complete responses, that creates additional copies
of the diagnostic information. Retention and access controls belong to the
host or consuming system.

## Transport and hosting

The application controls HTTP and HTTPS exposure, reverse proxies,
certificate validation, and network restrictions.

PingTrace does not configure TLS or enforce an HTTPS-only deployment.

The Aspire sample uses service discovery and a `https+http` address.
That sample configuration should not be described as an unconditional
HTTPS requirement.

Configure deployed diagnostic clients and endpoints according to the same
transport requirements as the information they carry.

A reverse proxy or authentication gateway can also affect what a successful
request proves. A response from an intermediary is not automatically
evidence that the application executed a fresh diagnostic operation.

## Caching

PingTrace does not currently apply explicit no-cache response headers or
configure an output-cache policy.

The host should ensure that diagnostic responses are not unintentionally
served from a cache when monitoring requires a fresh observation.

Caching can also retain trace information beyond the request that produced
it. Review proxy, application, and browser behavior according to the
deployment's requirements.

## Failures and error responses

Empty destination requests return a generic validation message. Routing failures
return structured errors that can include destination aliases, candidate paths,
and discovery issues. Handled diagnostic failures return selected messages in
TraceResult; unexpected exceptions propagate to the host's exception handling.

The host determines whether responses contain stack traces, internal
addresses, exception details, or other diagnostic information.

The HTTP adapter does not return a remote error body as a successful trace
result. Unsuccessful remote HTTP responses remain failures.

The current destination handler translates `KeyNotFoundException` to
status 404 around the whole destination operation. Custom connectors
should account for that behavior when choosing exception types.

## Request cost

Ping and local Trace do not invoke dependency tracing methods.

Destination Trace performs the work implemented by the selected connector.
HTTP client resilience policies may add retries or other work.

The library does not provide its own request-rate limiter, concurrency
limit, or traversal budget.

The host owns traffic limits and timeout policy appropriate to the
application and its dependencies.

Cancellation is propagated through the runtime and built-in HTTP adapter,
but custom connectors must cooperate with it.

TraceAll recursively invokes explicitly registered dependencies. The default
runtime detects repeated node identities on each path, limits traversal depth,
and applies a cooperative execution deadline.

These controls do not impose a separate maximum result count, request count,
or request rate. Hosts remain responsible for endpoint authorization and
appropriate usage limits. Custom connectors must honor cancellation.

Both TraceAll endpoints belong to the group returned by `MapPingTrace`, so
authorization applied to that group also applies to recursive requests.

## Custom implementation responsibilities

Applications implementing `IPingTrace` or `IPingTraceConnector` should:

- Preserve the intended lightweight behavior of Ping.
- Avoid business mutations in diagnostic operations.
- Distinguish caller observations from dependency-reported information.
- Propagate cancellation.
- Use application-approved credentials and destination configuration.
- Return only diagnostic information appropriate for the endpoint's audience.

Connectors execute inside the application process. They are not isolated
by a plugin sandbox.

## Reporting a vulnerability

For reporting instructions, see the repository's
[security policy](https://github.com/patware/Patware.PingTrace/blob/main/SECURITY.md).

## Related documentation

- [Architecture](ARCHITECTURE.md)
- [Execution model](PINGTRACE.md)
- [Extension contracts](PLUGIN-ARCHITECTURE.md)
- [Distributed hosting](DISTRIBUTED-HOSTING.md)
- [Capability boundaries](../design/CAPABILITY-BOUNDARIES.md)

## Discovery and routed requests in 0.3.0

Discover and TraceRoute inherit the same route-group authorization as all other
PingTrace endpoints. Discovery exposes registered topology; routing errors may
include candidate paths and branch issues. Apply the host's access policy to
both discovery and execution, and configure outgoing clients with the required
credentials. Knowledge of a discovered route does not authorize its execution.

Discovery limits depth, time, report size, and HTTP response buffering. These
are cooperative per-request controls, not a global rate limiter or concurrency
budget. An indirect name lookup can contact multiple participating applications.
See [discovery and routing](DISCOVERY-AND-ROUTING.md).
