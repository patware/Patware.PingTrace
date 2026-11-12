# PingTrace.Adapter.RedisCache

Redis connectivity diagnostics using an existing `IConnectionMultiplexer`
registration.

## Registration and ownership

Register the application's connection separately, then register a destination:

```csharp
using PingTrace.Adapter.RedisCache;
using PingTrace.Runtime;

services.AddPingTrace(builder => builder.AddRedisCacheDestination("Cache"));
```

The scoped connector resolves the connection only when traced and does not
dispose it. All destinations registered through this overload resolve the same
unkeyed connection registration; aliases do not select different Redis servers.

## Results and failures

A completed ping returns `Status = Succeeded` and `Payload = "PONG"`. The
connector leaves machine and identity fields empty: successful connectivity
does not establish the responding server's identity or intended environment.

Redis client timeouts and diagnostic timeouts return failed `Timeout` results.
Connection exceptions distinguish authentication failures from other connection
failures. Server errors return `RemoteError`; other handled Redis exceptions
return `Unknown`. Failures include `FailureKind`, `ErrorCode`, and `Message`.

Each failed result is logged at Warning level with its destination, error code,
and message. The original exception is attached to the log entry rather than
copied into the public result. The host controls logging providers, filtering,
and retention; registering logging services alone does not store logs.

Caller cancellation and unexpected errors, including a missing connection
registration, propagate. The five-second wait bounds waiting for the ping task;
it does not bound synchronous connection resolution or cancel the Redis command.
The Redis client's own timeout may expire independently of this wait.

## Discovery and indirect tracing in 0.3.0

This connector is a leaf. Discovery lists its registered alias without executing
its diagnostic operation. A remote application can reach it through discovery and
routed HTTP tracing when the owning application exposes the new endpoints. Its
name remains the alias assigned by that owner. Discovery does not establish health;
inspect the trace result's status and failure details when the probe executes.

See [discovery and routing](https://github.com/patware/Patware.PingTrace/blob/main/docs/architecture/DISCOVERY-AND-ROUTING.md).
