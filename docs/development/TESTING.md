# Testing

Great unit tests document themselves.

Each test method name should clearly describe the behavior it verifies.
The test code is the authoritative description of coverage; this guide
does not duplicate that inventory.

## Projects

Library tests live in projects corresponding to the libraries they exercise:

- `PingTrace.Runtime.Tests`
- `PingTrace.Adapter.HttpClient.Tests`
- `PingTrace.AspNetCore.Tests`
- `PingTrace.Adapter.EntityFramework.Tests`
- `PingTrace.Adapter.MicrosoftGraph.Tests`
- `PingTrace.Adapter.ActiveDirectory.Tests`
- `PingTrace.Adapter.RedisCache.Tests`

The adapter-to-endpoint round-trip test currently lives in
`PingTrace.AspNetCore.Tests`.

The Aspire sample has a separate test project under `SampleApplications`.

## Run tests

The library projects use MSTest.Sdk and target .NET 10.
The repository's `global.json` selects Microsoft.Testing.Platform.

Run commands from the repository root.

To run one suite:

```powershell
dotnet test --project tests/PingTrace.Runtime.Tests/PingTrace.Runtime.Tests.csproj --configuration Release -- --minimum-expected-tests 1
```

To run all library suites:

```powershell
$testProjects = @(
    Get-ChildItem ./tests -Recurse -Filter *.csproj |
        Sort-Object FullName
)

if ($testProjects.Count -eq 0) {
    throw "No library test projects were found."
}

foreach ($project in $testProjects) {
    dotnet test --project $project.FullName --configuration Release -- --minimum-expected-tests 1

    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed: $($project.FullName)"
    }
}
```

The minimum-test requirement prevents an empty suite from being mistaken
for successful verification.

This loop excludes the Aspire sample's test project.

## Name tests for the behavior they verify

A reader should understand a test's purpose from its method name without
opening its implementation.

Use a consistent pattern such as:

```text
Operation_Condition_ExpectedBehavior
```

Omit the condition when it adds no useful information.

For example:

```text
DestinationTrace_UnknownDestination_ThrowsWithoutInvokingConnector
LocalTrace_DoesNotInvokeDependencies
Registration_ExistingImplementation_IsPreserved
```

Prefer meaningful behavior over implementation details. Avoid names such
as `Test1`, `Works`, or `ReturnsExpectedResult`.

When behavior changes, update the test name and assertions together.

## Make the test body readable

Separate each test into:

```csharp
// Arrange

// Act

// Assert
```

Keep setup focused on the scenario. Use descriptive variable names and
assertions that make the expected outcome clear.

Use helpers to remove distracting repetition, but keep the important
conditions and assertions visible in the test.

A test should communicate one coherent behavior. Multiple assertions are
appropriate when they describe that behavior.

## Document intent where needed

Include XML documentation for publicly visible test classes and members
to avoid missing-documentation warnings.

Keep summaries concise. Use remarks to explain reasoning that the method
name cannot express, such as why a test uses a completion signal instead
of measuring elapsed time.

Comments should explain intent or constraints rather than narrate
straightforward code.

## Test public behavior

Prefer observable outcomes over private implementation details.

Exercise adapters through their public registration APIs where practical.
This verifies the same path an application uses without exposing internal
classes solely for testing.

When a contract changes, update producers, consumers, tests, and
documentation together. A failing assertion may identify an incompatible
change rather than an obsolete test.

## Keep tests isolated

Library tests use substitutes, in-memory HTTP handlers, and ASP.NET Core
TestServer. They do not require running external services.

Each test should own its mutable state and resources. Avoid changing
process-wide environment or configuration.

Each library test project enables method-level parallelization.
Tests must remain independent of execution order and other tests.

Dispose resources according to their ownership, including service scopes,
providers, clients, responses, cancellation sources, and test hosts.

EF adapter tests use an in-memory SQLite provider for relational infrastructure.
A command interceptor substitutes readers or failures before the SQL
Server-specific command executes. The substituted reader must match EF's
generated projection order. These tests do not validate SQL Server syntax,
connectivity, authentication, or permissions.

Graph adapter tests keep the real connector and Graph SDK while substituting
the HTTP transport. Anonymous authentication avoids token acquisition. This
checks the SDK/adapter boundary without a tenant; it does not validate live
authentication or permissions. Keep simulation code in tests or sample hosts,
and use the application's authenticated client in production.

See the [sample walkthrough](GETTING-STARTED.md#exercise-the-simulated-microsoft-graph-destination)
for interactive success, error, and delay scenarios.

AD tests replace the directory reader while exercising the real connector.
They cover mapping, deferred resolution, failures, incomplete results, and
cooperative cancellation. They do not test the built-in LDAP reader's bind,
queries, parsing, or native authentication. Cancellation of a simulated reader
does not prove interruption of synchronous LDAP calls. See the
[AD simulation walkthrough](GETTING-STARTED.md#exercise-the-simulated-active-directory-destination).

## Make asynchronous tests deterministic

Pass `TestContext.CancellationToken` to asynchronous operations that
support it.

For cancellation scenarios, use a dedicated cancellation source linked
to the test context.

Coordinate asynchronous work with explicit signals rather than sleeps.
Use bounded waits to prevent indefinite hangs.

Do not assert behavior based on machine speed, scheduler timing, or
arbitrary latency thresholds.

## Separate library and application testing

The Aspire sample tests start application resources and require an
environment capable of running the sample.

Keep those tests separate from the isolated library suites.

In-memory tests do not replace deployment checks for real networking,
authentication, TLS, service discovery, or hosting configuration.

## Coverage and verification

Coverage reports supplement readable tests and meaningful assertions.
They do not replace them.

Use coverage tooling compatible with Microsoft.Testing.Platform.
Do not assume legacy VSTest settings configure the current runner.

Report coverage figures only from an actual report, and identify the
test run and source revision when relevant.

When reporting verification, state which projects and configuration were
run. Distinguish library tests from application, deployment, and package
checks.

## Related documentation

- [Development setup](GETTING-STARTED.md)
- [Development conventions](CODING-STANDARDS.md)
- [Architectural principles](../architecture/ARCHITECTURAL-PRINCIPLES.md)
