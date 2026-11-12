using Microsoft.Extensions.Logging;
using PingTrace.Tests;
using System.Text.Json;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using PingTrace.Core;
using PingTrace.Runtime;

namespace PingTrace.Adapter.ActiveDirectory.Tests;

/// <summary>
/// Verifies directory destinations through public registration.
/// </summary>
/// <remarks>
/// A substituted reader exercises connector behavior without native LDAP
/// operations. These tests do not validate the production LDAP reader.
/// </remarks>
[TestClass]
public sealed class ActiveDirectoryPingTraceConnectorTests
{
    /// <summary>
    /// Gets or sets the context supplied by MSTest.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Verifies diagnostic mapping and token forwarding.
    /// </summary>
    [TestMethod]
    public async Task SuccessfulRead_ReturnsDomainDiagnostics()
    {
        // Arrange
        CancellationToken observedToken = default;

        using ServiceProvider provider = CreateProvider(token =>
        {
            observedToken = token;

            return Task.FromResult(new ActiveDirectoryDiagnostics
            {
                DomainController = "dc01.contoso.example",
                DomainDistinguishedName = "DC=contoso,DC=example",
                DomainObjectName = "contoso"
            });
        });

        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        IReadOnlyList<TraceResult> results = await sut.TraceAsync("ACTIVEDIRECTORY", TestContext.CancellationToken);

        // Assert
        observedToken.Should().Be(TestContext.CancellationToken);
        results.Should().HaveCount(2);

        TraceResult dependency = results[1];
        dependency.Status.Should().Be(TraceStatus.Succeeded);
        dependency.Name.Should().Be("ActiveDirectory");
        dependency.MachineName.Should().Be("dc01.contoso.example");
        dependency.Identity.Should().Be(@"CONTOSO\sample-app");
        dependency.FinishedAt.Should().NotBe(default);

        using JsonDocument payload = JsonDocument.Parse(dependency.Payload);

        payload.RootElement
            .GetProperty("DomainDistinguishedName")
            .GetString().Should().Be("DC=contoso,DC=example");
    }

    /// <summary>
    /// Verifies that local diagnostics do not resolve a reader.
    /// </summary>
    [TestMethod]
    public async Task LocalOperations_DoNotResolveReader()
    {
        // Arrange
        ServiceCollection services = new();

        services.AddPingTrace(builder =>
        {
            builder.AddActiveDirectoryDestination<StubReader>("ActiveDirectory", @"CONTOSO\sample-app");
        });

        using ServiceProvider provider = BuildProvider(services);
        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        string ping = sut.Ping();
        TraceResult local = await sut.TraceAsync(TestContext.CancellationToken);

        // Assert
        ping.Should().Be("Pong");
        local.Name.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies preservation of the reader's original failure.
    /// </summary>
    [TestMethod]
    public async Task FailedRead_PropagatesOriginalException()
    {
        // Arrange
        IOException failure = new("Directory unavailable.");

        using ServiceProvider provider = CreateProvider(_ =>
            Task.FromException<ActiveDirectoryDiagnostics>(failure));

        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        Func<Task> act = () => sut.TraceAsync(
            "ActiveDirectory",
            TestContext.CancellationToken);

        // Assert
        var assertion = await act.Should().ThrowAsync<IOException>();
        assertion.Which.Should().BeSameAs(failure);
    }

    /// <summary>
    /// Verifies rejection of incomplete reader results.
    /// </summary>
    [TestMethod]
    public async Task IncompleteRead_IsRejected()
    {
        // Arrange
        using ServiceProvider provider = CreateProvider(_ =>
            Task.FromResult(new ActiveDirectoryDiagnostics
            {
                DomainController = string.Empty,
                DomainDistinguishedName = "DC=contoso,DC=example",
                DomainObjectName = "contoso"
            }));

        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        IReadOnlyList<TraceResult> results = await sut.TraceAsync(
            "ActiveDirectory",
            TestContext.CancellationToken);

        // Assert
        results[1].Status.Should().Be(TraceStatus.Failed);
        results[1].FailureKind.Should().Be(TraceFailureKind.InvalidResponse);
        results[1].ErrorCode.Should().Be("Directory.IncompleteDiagnostics");
        results[1].Message.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies cancellation while the reader is executing.
    /// </summary>
    /// <remarks>
    /// This verifies connector cancellation with a cooperative reader.
    /// It does not establish interruption of synchronous native LDAP calls.
    /// </remarks>
    [TestMethod]
    public async Task CancellationDuringRead_Propagates()
    {
        // Arrange
        TaskCompletionSource<bool> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);

        using ServiceProvider provider = CreateProvider(async token =>
        {
            entered.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);

            throw new InvalidOperationException("The delay must be canceled.");
        });

        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        Task<IReadOnlyList<TraceResult>> pending = sut.TraceAsync("ActiveDirectory", cancellation.Token);

        try
        {
            await entered.Task.WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.CancellationToken);
        }
        finally
        {
            cancellation.Cancel();
        }

        Func<Task> act = async () =>
        {
            await pending.WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.CancellationToken);
        };

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Verifies LDAP classifications and preservation of the original exception in logs.
    /// </summary>
    /// <param name="code">The LDAP client error code.</param>
    /// <param name="kind">The expected failure classification.</param>
    [TestMethod]
    [DataRow(49, TraceFailureKind.Authentication)]
    [DataRow(50, TraceFailureKind.Authorization)]
    [DataRow(85, TraceFailureKind.Timeout)]
    [DataRow(81, TraceFailureKind.Connection)]
    [DataRow(91, TraceFailureKind.Connection)]
    [DataRow(82, TraceFailureKind.Unknown)]
    public async Task LdapFailure_ReturnsResultAndLogsOriginalException(int code, TraceFailureKind kind)
    {
        // Arrange
        using CapturingLoggerProvider logs = new();
        System.DirectoryServices.Protocols.LdapException exception = new(code, "Internal LDAP details");
        using ServiceProvider provider = CreateProvider(
            _ => Task.FromException<ActiveDirectoryDiagnostics>(exception), logs);
        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        IReadOnlyList<TraceResult> results = await sut.TraceAsync("ActiveDirectory", TestContext.CancellationToken);

        // Assert
        results[1].Status.Should().Be(TraceStatus.Failed);
        results[1].FailureKind.Should().Be(kind);
        results[1].ErrorCode.Should().Be($"Ldap.Client.{code}");
        results[1].Message.Should().NotContain("Internal LDAP details");
        results[1].Payload.Should().BeEmpty();
        logs.Warnings.Should().ContainSingle();
        logs.Warnings.Single().Exception.Should().BeSameAs(exception);
        logs.Warnings.Single().Message.Should().Contain($"Ldap.Client.{code}");
    }

    private static ServiceProvider CreateProvider(
        Func<CancellationToken, Task<ActiveDirectoryDiagnostics>> read,
        CapturingLoggerProvider? logs = null)
    {
        ServiceCollection services = new();
        if (logs is not null)
        {
            services.AddLogging(builder => builder.AddProvider(logs));
        }

        services.AddScoped<StubReader>(_ => new StubReader(read));

        services.AddPingTrace(builder =>
        {
            builder.AddActiveDirectoryDestination<StubReader>(
                "ActiveDirectory",
                @"CONTOSO\sample-app");
        });

        return BuildProvider(services);
    }

    private static ServiceProvider BuildProvider(IServiceCollection services)
    {
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    private sealed class StubReader(Func<CancellationToken, Task<ActiveDirectoryDiagnostics>> read) : IActiveDirectoryDiagnosticsReader
    {
        public Task<ActiveDirectoryDiagnostics> ReadDomainAsync(CancellationToken cancellationToken = default)
        {
            return read(cancellationToken);
        }
    }
}
