# Microsoft Graph adapter

`PingTrace.Adapter.MicrosoftGraph` reads organization metadata through an
application-registered `GraphServiceClient`. It targets .NET 10.

## Register a destination

Reference this project or its package, register the application's authenticated
`GraphServiceClient` in dependency injection, then add the destination:

```csharp
using PingTrace.Adapter.MicrosoftGraph;
using PingTrace.Runtime;

services.AddPingTrace(pingTrace =>
{
    pingTrace.AddGraphOrganizationDestination(
        "MicrosoftGraph",
        $"tenant:{tenantId}; app:{clientId}");
});
```

`services`, `tenantId`, and `clientId` come from the host application. The host
owns credential selection, token acquisition, tenant selection, and permissions.
Use the same client configuration as application operations so the diagnostic
read exercises the intended connection. Registration does not authenticate or
contact Graph.

The current extension resolves the unkeyed `GraphServiceClient` registration.
It accepts a fixed identity label; it does not resolve a delegated user's
identity or select a separate client for each destination.

With the ASP.NET Core endpoints mapped, request:

```http
GET /pingtrace/Trace?destination=MicrosoftGraph
```

The response contains the application's result followed by the Graph result.
`TraceAll` also invokes this connector as a leaf dependency. Ping and local
Trace do not resolve the Graph client or make the organization request.

## Operation and result

The connector calls `GET /organization` through the SDK, selecting `id`,
`displayName`, and `verifiedDomains`. See Microsoft's
[organization API documentation](https://learn.microsoft.com/en-us/graph/api/organization-list?view=graph-rest-1.0)
for permission requirements.

| Field | Meaning |
| --- | --- |
| `Name` | Locally registered destination name |
| `MachineName` | Host from the client's configured absolute base URL; not a physical Graph server |
| `Identity` | Non-secret configured identity label supplied during registration |
| `Payload` | JSON-encoded string containing `TenantId`, `DisplayName`, `VerifiedDomains`, `IdentitySource`, and `Probe` |
| Timing | Application-measured interval including client resolution and the organization request |

`IdentitySource` is `ConfiguredAuthentication` and `Probe` is
`OrganizationRead`. The identity label is not a server-returned assertion of
the authenticated principal. Do not put credentials or tokens in it.

Payload remains a string under the current Core contract, so its JSON appears
escaped inside an HTTP trace response. A successful organization read does not
establish Teams access, access to other Graph resources, or write permissions.

## Lifetime, validation, and failures

The connector is scoped and resolves the client only when traced. It does not
dispose the application-owned client. Cancellation is passed to the SDK.

The base URL must be absolute and contain a host. The response must contain
exactly one non-null organization with a nonblank ID. An invalid base URL returns
a failed `Configuration` result; invalid organization data returns `InvalidResponse`.
Handled API errors classify HTTP 401 as `Authentication`, 403 as `Authorization`,
and other HTTP error statuses as `RemoteError`. Transport failures, recognized
HTTP client timeouts, and JSON parsing errors also become failed results.

Failures carry `Status`, `FailureKind`, `ErrorCode`, and `Message`. Successful
organization metadata remains in `Payload`. Every locally generated failure is
logged at Warning level; caught exceptions are attached to preserve their original
details. The host controls providers, filtering, and retention. Logging service
registration alone does not retain logs.

Cancellation and unexpected exceptions propagate. In particular, arbitrary token
acquisition failures from application-supplied authentication providers are not
automatically classified by this adapter.

The ASP.NET Core endpoint returns HTTP 200 when it successfully produces a
diagnostic report containing a handled Graph failure. Consumers must inspect the
result status; a Graph permission failure does not become an HTTP 403 from PingTrace.

## Develop without a tenant

The test project uses the real Graph SDK and connector with an in-memory HTTP
handler and anonymous authentication. This exercises request construction,
deserialization, mapping, error handling, cancellation, deferred resolution,
and client ownership without token acquisition or network access.

Run from the repository root:

```powershell
dotnet test --project tests/PingTrace.Adapter.MicrosoftGraph.Tests/PingTrace.Adapter.MicrosoftGraph.Tests.csproj --configuration Release -- --minimum-expected-tests 1
```

The Aspire API sample uses a separate sample-only handler with configurable
responses. Follow the [sample simulation walkthrough](https://github.com/patware/Patware.PingTrace/blob/main/docs/development/GETTING-STARTED.md#exercise-the-simulated-microsoft-graph-destination).

These simulations do not validate real authentication, permissions, DNS, TLS,
or Microsoft service behavior. Live integration verification requires an
appropriately configured tenant and client. Production applications register
the real authenticated client; there is no automatic fallback to simulation.

## Discovery and indirect tracing in 0.3.0

This connector is a leaf. Discovery lists its registered alias without executing
its diagnostic operation. A remote application can reach it through discovery and
routed HTTP tracing when the owning application exposes the new endpoints. Its
name remains the alias assigned by that owner. Discovery does not establish health;
inspect the trace result's status and failure details when the probe executes.

See [discovery and routing](https://github.com/patware/Patware.PingTrace/blob/main/docs/architecture/DISCOVERY-AND-ROUTING.md).
