# Monitoring and observability

PingTrace provides diagnostic responses for monitoring systems and people
investigating applications.

Its primary questions are:

- Is the application responding?
- Which application and machine answered?
- Which runtime identity does it report?
- Does a configured dependency return the expected observations?
- Does a delay appear when that dependency is involved?

PingTrace does not currently schedule requests, store diagnostic history,
evaluate health thresholds, or publish its own telemetry events.

## Monitoring responsibilities

PingTrace supplies observations. The consuming application or monitoring
system interprets them.

| Responsibility | Owner |
| --- | --- |
| Return Ping and Trace responses | PingTrace implementation |
| Select destinations and configure connections | Application |
| Schedule probes | Monitoring system or application |
| Define expected names, identities, payloads, and durations | Application or monitoring configuration |
| Classify health and raise alerts | Monitoring system or application |
| Restart, scale, or reroute workloads | Hosting and operations systems |
| Store historical observations | Consuming system |

A successful HTTP status establishes successful execution of that request.
It does not automatically establish that the reported application, machine,
identity, or payload matches operational expectations.

## Four levels of checking

Different questions justify different costs and polling intervals.

The intervals below describe the intended usage model. They are examples,
not defaults enforced by the library.

| Level | Example interval | Purpose | PingTrace position |
| --- | --- | --- | --- |
| A | Every second | Obtain a minimal endpoint response | Explicit HEAD support on Ping is planned |
| B | Every 10 seconds | Confirm that application code responds predictably | Ping is implemented |
| C | Every minute | Verify dependency connections | Named destination Trace and recursive TraceAll are implemented |
| D | Every hour | Exercise representative business behavior safely | Application-owned synthetic or what-if checks |

A high-frequency probe can be appropriate when its work is deliberately
small. Ping returns a fixed string without invoking dependency tracing.

Destination Trace costs more because it invokes a connector and may involve
networking, authentication, serialization, and remote work.

Choose intervals according to actual cost and the number of callers.
Several monitoring systems probing the same frontend can multiply the
traffic reaching its dependencies.

The library does not implement polling, deduplication, or probe coordination.

## Ping observations

The default Ping response is:

```text
Pong
```

A monitoring check can verify the expected status and exact response body.

Ping establishes that the application endpoint can answer. It does not
test dependencies or business operations.

The ASP.NET Core adapter explicitly maps GET for Ping. Do not configure a monitor
to depend on PingTrace HEAD support until that support is implemented
and verified.

## Trace observations

The default runtime reports:

| Observation | Diagnostic use |
| --- | --- |
| Application name | Identify the responding application |
| Machine name | Identify the reported execution environment |
| Runtime identity | Investigate process-account configuration |
| Payload | Inspect implementation-specific diagnostic information |
| Start and finish timestamps | Place an observation in time |
| Elapsed time | Compare the duration of diagnostic operations |

The default application name comes from the managed entry assembly.

The reported runtime identity is not necessarily the credential used for
an outgoing HTTP or database connection.

Payload is currently a string. Local Trace reports the managed entry
assembly's version, falling back to `Pong` when unavailable. A dependency
trace's local result uses `{version} --> [{destination}]`.

Connector payloads describe the dependency. The SQL Server connector returns
the current database name. Structured object payloads remain planned work.

## Deployment verification

Diagnostic checks are useful immediately after deployment because they can
separate hosting problems from application and dependency problems.

An application can provide several checks that exercise progressively
different parts of the deployment:

| Check | What a successful request helps establish |
| --- | --- |
| Application-supplied `ping.html` | The requested static HTML resource can be delivered |
| Application-supplied `ping.png` | The requested image can be delivered with the expected content type |
| Application Ping endpoint | Application code receives the request and returns its expected response |
| Local Trace | The responding process reports its application, machine, identity, and payload |
| Destination Trace | The configured connector obtains diagnostic results from a dependency |

These are complementary checks. Static resources are deployment assets
provided by the application; PingTrace does not create them.

A static-file response can help investigate path and access configuration,
but it does not uniquely prove which account read the file. Hosting
configuration, anonymous-access settings, proxies, and caches affect the
request path.

Likewise, serving an image with the expected media type establishes delivery
behavior. It does not prove that a particular image-processing module ran.

For IIS deployments, use these observations alongside the actual site,
application-pool, filesystem, and handler configuration.

## Detecting an unexpected destination

A frontend can respond successfully while its dependency configuration
points to the wrong environment or application.

For example:

```text
Expected: Frontend → Production API
Observed: Frontend → Test API
```

A successful dependency trace can reveal the mismatch through the remote
application name, machine, or diagnostic payload.

The local destination alias is not proof of the remote system's identity.
An alias such as `ProductionApi` is simply the configured lookup name.

Compare the returned observations with expectations maintained by the
application or monitoring system.

The current runtime does not perform those comparisons automatically.

## Timing boundaries

The current `TraceResult` records `StartedAt` in its constructor and
`FinishedAt` when `Finish` is called.

Its elapsed value is:

```text
FinishedAt - StartedAt
```

For local Trace, that interval covers local diagnostic result construction
and completion.

For destination Trace, it also includes the awaited connector operation
and collection of the returned results.

It does not measure the entire incoming HTTP request. Work before result
construction and after completion, including final response serialization,
falls outside the local result's interval.

### Overlapping durations

Consider an illustrative successful request:

| Observation | Elapsed |
| --- | --- |
| Frontend destination trace | 25 ms |
| API local trace returned by that request | 1 ms |

The API operation occurs within the frontend's dependency call.

Adding the durations would double-count overlapping work.

Subtracting them gives an approximate view of work outside the API's
reported local interval. It does not isolate network transit.

Connection setup, authentication, serialization, scheduling, resilience
handlers, and other processing may contribute to that difference.

### Clocks

The implementation currently uses `DateTime.Now`.

Clock adjustments can affect elapsed calculations. Timestamps from different
machines also need not be synchronized.

Do not calculate one-way network delay by subtracting timestamps recorded
on different machines.

A monotonic timer for duration measurement is a proposed improvement,
not a current capability.

### Client-observed duration

A monitoring tool may separately measure how long its complete HTTP request
takes.

That duration includes work outside the server's reported Trace interval.
Both measurements are useful, but they describe different boundaries.

Record which measurement a threshold uses.

## Investigating a slow dependency path

Consider this conceptual application chain:

```text
Frontend → Backend → Database
```

The following workflow helps narrow an investigation:

1. Request a local Trace from the frontend.
2. Request a destination Trace from the frontend toward the backend.
3. Request a local Trace directly from the backend.
4. Run the backend's database diagnostic operation.
5. Compare with an appropriate diagnostic performed directly against
   the database.

If local operations are fast but the backend-to-database operation is slow,
investigate work associated with that dependency call.

Possible contributors include connection establishment, authentication,
network routing, firewall behavior, connection-pool waits, and database work.

A fast direct database check does not prove that all database operations
are fast. Its value comes from comparing the operations and execution
paths actually exercised.

The current repository supplies an HTTP connector, not a database connector.
The backend-to-database step requires an application-specific implementation
or a future adapter.

## Routine probes and on-demand investigation

Routine monitoring should perform enough work to answer its operational
question at an acceptable cost.

A deeper investigation may issue several separate probes to compare paths.
For example:

```text
A
A → B
A → B → C
A → B → D
A → E
```

Use a `TraceRouteRequest.Segments` array for an explicit path; the runtime does
not parse textual path expressions. Named Trace can discover an indirect route.

For direct destination Trace, the HTTP connector requests the immediate target's
local Trace. TraceAll instead calls the remote recursive endpoint and visits
its registered dependencies.

Explicit multi-hop destination routing is available in 0.3.0. TraceAll follows
registered connectors rather than a caller-supplied destination path. Shared
dependencies are visited per path, and parent durations include descendant work;
do not add overlapping durations to calculate total latency.

A future graph viewer must preserve path or parent-child relationships.
The current flat result array alone does not describe a branching graph.

## Resilience affects observations

The Aspire sample configures standard resilience for factory-created
HTTP clients.

The frontend's PingTrace client removes resilience handlers and uses a five-second
client timeout. Other clients can retain their host-configured policies.

A successful destination trace may therefore include more than one
connection attempt or other resilience behavior. The reported local
duration includes the time spent awaiting the configured client operation.

PingTrace results do not currently report attempt counts.

Use the host's HTTP telemetry when an investigation needs to distinguish
initial failure, retry, and eventual success.

## Host telemetry

The sample configures:

- OpenTelemetry logging integration.
- ASP.NET Core instrumentation.
- HTTP client instrumentation.
- Runtime metrics.
- OTLP export when an exporter endpoint is configured.

These are sample-host capabilities, not telemetry facilities implemented
by the PingTrace libraries.

The sample's incoming-request tracing filter excludes `/health` and
`/alive`. It does not explicitly exclude `/pingtrace`.

Consequently, PingTrace traffic is eligible for the host's normal HTTP
instrumentation, subject to its sampling and export configuration.

That does not mean every diagnostic response is automatically stored,
nor that response payloads are automatically included in telemetry.

The libraries currently define no dedicated PingTrace activity source,
metrics meter, stored-log service, or lifecycle event stream.

## Diagnostic history

The current implementation returns observations to the caller and does not
persist them.

A consumer building historical views should retain enough context to
interpret an observation, such as:

- The caller's request time.
- The endpoint and destination alias used.
- The returned application and machine names.
- Success or failure.
- Client-observed duration.
- Returned trace results, where appropriate.

Apply the host's access and retention policies to stored diagnostic data.
Trace results can contain execution identities and sensitive payloads.

## Relationship to health checks

PingTrace provides observations that can inform a health decision.

An application's health policy determines whether an unexpected destination,
a failed dependency call, or an elevated duration means degraded,
unhealthy, or something else.

The current `IPingTrace` contract does not inherit from `IHealthCheck`,
and no built-in adapter performs that classification.

Similarly, PingTrace does not configure Docker or Kubernetes probes,
restart policies, or scaling rules.

These systems can consume or complement diagnostic endpoints according
to the application's operational requirements.

## Related documentation

- [Execution model](PINGTRACE.md)
- [Distributed hosting](DISTRIBUTED-HOSTING.md)
- [Security](SECURITY.md)
- [Definitions](PINGTRACE-DEFINITIONS.md)
- [Capability boundaries](../design/CAPABILITY-BOUNDARIES.md)
