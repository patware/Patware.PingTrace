# Active Directory adapter

`PingTrace.Adapter.ActiveDirectory` reports selected domain diagnostics through
an application-registered `IActiveDirectoryDiagnosticsReader`. It targets
.NET 10 and includes an LDAP implementation using
`System.DirectoryServices.Protocols`.

## Register a destination

Register the reader and its configured connection separately, then register
the destination:

```csharp
using Microsoft.Extensions.DependencyInjection;
using PingTrace.Adapter.ActiveDirectory;
using PingTrace.Runtime;

// Register the application's configured LdapConnection separately.
services.AddScoped<LdapActiveDirectoryDiagnosticsReader>();

services.AddPingTrace(pingTrace =>
{
    pingTrace.AddActiveDirectoryDestination<LdapActiveDirectoryDiagnosticsReader>(
        "ActiveDirectory",
        @"CONTOSO\application-account");
});
```

The host supplies `services` and the connection's server, authentication,
credentials, transport protection, and timeout settings. The identity label
must describe that configuration and must not contain passwords or tokens.
Applications can implement the public reader interface to reuse another
directory access layer. The generic registration resolves that concrete reader
type from dependency injection.

With the ASP.NET Core endpoints mapped, request:

```http
GET /pingtrace/Trace?destination=ActiveDirectory
```

The response contains the local application's result followed by the directory
result. `TraceAll` invokes the connector as a leaf. Ping and local Trace do not
resolve the reader or access the directory.

## LDAP operation

`LdapActiveDirectoryDiagnosticsReader` performs these operations on the supplied
connection:

1. Reject `AuthType.Anonymous` and bind with the configured credentials.
2. Perform a base-scope RootDSE search for `dnsHostName` and `defaultNamingContext`.
3. Perform a base-scope search at that naming context using
   `(objectClass=domainDNS)`, reading the domain object's `name` attribute.

Each search must return exactly one entry. Required attributes must have one
nonblank string value. Reading the domain object exercises domain data access
beyond RootDSE metadata. This is a small diagnostic read, not a full replacement
for every property returned by PowerShell's `Get-ADDomain`.

## Result fields

| Field | Meaning |
| --- | --- |
| `Name` | Local destination name |
| `MachineName` | Domain controller DNS hostname returned by the reader |
| `Identity` | Fixed configured identity label; not a server-returned identity assertion |
| `Payload` | JSON-encoded string with `DomainDistinguishedName`, `DomainObjectName`, `IdentitySource`, and `Probe` |
| Timing | Includes reader resolution and the diagnostic operation |

`IdentitySource` is `ConfiguredAuthentication`; `Probe` is `DomainRead`.
`DomainObjectName` is the domain object's `name` attribute, not necessarily its
DNS or NetBIOS name. The payload remains a string under the current Core contract.

Null or incomplete diagnostics become failed `InvalidResponse` results. The
built-in reader reports anonymous authentication as `Configuration` and invalid
entry counts or required attributes as `InvalidResponse`. Known LDAP client and
operation failures are classified from their error/result codes; unclassified
client errors remain `Unknown`.

Failures carry `Status`, `FailureKind`, `ErrorCode`, and an explanation in
`Message`. Every failure is logged at Warning level with the destination and
diagnostic details; handled exceptions are attached. The host must configure a
logging provider and filtering to retain these logs.

Reader resolution errors, unexpected custom-reader exceptions, and cancellation
still propagate. A successfully produced report containing a handled directory
failure is returned as HTTP 200 by the ASP.NET Core endpoint. Consumers inspect
the diagnostic status rather than treating HTTP success as dependency health.

## Connection ownership and cancellation

The connector is scoped and defers reader resolution until tracing begins.
Neither the connector nor the LDAP reader disposes application-owned resources.
The host must manage their lifetimes and give the reader exclusive use of its
connection while binding and reading. Do not run concurrent operations on that
connection.

The built-in LDAP reader uses synchronous `Bind` and `SendRequest` calls.
Returning a Task does not make them asynchronous. Cancellation is checked
between operations but cannot interrupt a native call already executing.
Configure a finite connection timeout; it is not a total probe deadline.
PingTrace's cooperative TraceAll deadline cannot forcibly stop these calls.

Use the host's intended authentication and transport configuration. For example,
LDAPS requires a controller configured for it and a trusted matching server
certificate. Native LDAP dependencies and authentication support vary by host
platform and require integration verification.

## Develop without a domain controller

Production and simulation use the same connector:

```text
Production: PingTrace -> connector -> LDAP reader -> domain controller
Sample:     PingTrace -> connector -> simulated reader
```

The tests substitute the reader and verify mapping, deferred resolution,
token forwarding, and cancellation with a cooperative reader. Failure assertions
must distinguish failed diagnostic results from unexpected exceptions that
propagate. They do not execute or validate
the LDAP reader's bind, queries, native response parsing, authentication,
permissions, or native cancellation behavior.

Run from the repository root:

```powershell
dotnet test --project tests/PingTrace.Adapter.ActiveDirectory.Tests/PingTrace.Adapter.ActiveDirectory.Tests.csproj --configuration Release -- --minimum-expected-tests 1
```

The Aspire sample has a separate simulated reader with configurable scenarios.
See the [AD simulation walkthrough](https://github.com/patware/Patware.PingTrace/blob/main/docs/development/GETTING-STARTED.md#exercise-the-simulated-active-directory-destination).
Real-domain verification remains separate; successful simulated tests do not
establish that the production LDAP integration works in a deployment.

## Discovery and indirect tracing in 0.3.0

This connector is a leaf. Discovery lists its registered alias without executing
its diagnostic operation. A remote application can reach it through discovery and
routed HTTP tracing when the owning application exposes the new endpoints. Its
name remains the alias assigned by that owner. Discovery does not establish health;
inspect the trace result's status and failure details when the probe executes.

See [discovery and routing](https://github.com/patware/Patware.PingTrace/blob/main/docs/architecture/DISCOVERY-AND-ROUTING.md).
