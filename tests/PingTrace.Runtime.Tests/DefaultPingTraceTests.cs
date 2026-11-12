using System.Reflection;

using FluentAssertions;

using NSubstitute;

using PingTrace.Core;

namespace PingTrace.Runtime.Tests;

/// <summary>
/// Verifies local and destination tracing behavior.
/// </summary>
/// <remarks>
/// Tests use controlled connector responses rather than network calls
/// or timing thresholds.
/// </remarks>
[TestClass]
public sealed class DefaultPingTraceTests
{
    /// <summary>
    /// Gets or sets the context supplied by MSTest.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    private static string ApplicationName => Assembly.GetEntryAssembly()!.GetName().Name!;
    private static string ApplicationVersion => Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Pong";

    /// <summary>
    /// Verifies that Ping and local Trace do not invoke dependencies.
    /// </summary>
    [TestMethod]
    public async Task LocalOperations_DoNotInvokeConnectors()
    {
        // Arrange
        IPingTraceConnector connector = CreateConnector("ApiService");
        DefaultPingTrace sut = new(new[] { connector });

        // Act
        string ping = sut.Ping();
        TraceResult trace = await sut.TraceAsync(TestContext.CancellationToken);

        // Assert
        ping.Should().Be("Pong");
        trace.Name.Should().Be(ApplicationName);
        trace.MachineName.Should().Be(Environment.MachineName);
        trace.Identity.Should().Be(
            $"{Environment.UserDomainName}\\{Environment.UserName}");
        trace.Payload.Should().Be(ApplicationVersion);
        trace.StartedAt.Should().NotBe(default);
        trace.FinishedAt.Should().NotBe(default);

        await connector.DidNotReceive()
            .TraceAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies case-insensitive tracing of the local application by name.
    /// </summary>
    [TestMethod]
    public async Task NamedLocalTrace_ReturnsSingleLocalResult()
    {
        // Arrange
        IPingTraceConnector connector = CreateConnector("ApiService");
        DefaultPingTrace sut = new(new[] { connector });

        // Act
        IReadOnlyList<TraceResult> results = await sut.TraceAsync(ApplicationName.ToUpperInvariant(), TestContext.CancellationToken);

        // Assert
        TraceResult result = results.Should().ContainSingle().Subject;
        result.Name.Should().Be(ApplicationName);
        result.Payload.Should().Be(ApplicationVersion);

        await connector.DidNotReceive()
            .TraceAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies connector selection, result ordering, and token propagation.
    /// </summary>
    [TestMethod]
    public async Task DestinationTrace_InvokesOnlySelectedConnector()
    {
        // Arrange
        TraceResult remote = CreateResult("RemoteApi");
        TraceResult database = CreateResult("Database");

        IPingTraceConnector selected = CreateConnector("ApiService");
        IPingTraceConnector other = CreateConnector("OtherService");

        selected.TraceAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TraceResult>>(
                new[] { remote, database }));

        DefaultPingTrace sut = new(new[] { selected, other });
        CancellationToken token = TestContext.CancellationToken;

        // Act
        IReadOnlyList<TraceResult> results =
            await sut.TraceAsync("apiservice", token);

        // Assert
        results.Select(result => result.Name)
            .Should().Equal(ApplicationName, "RemoteApi", "Database");

        results[0].Payload.Should().Be($"{ApplicationVersion} --> [apiservice]");
        results[0].FinishedAt.Should().NotBe(default);
        results[1].Should().BeSameAs(remote);
        results[2].Should().BeSameAs(database);

        await selected.Received(1).TraceAsync(token);
        await other.DidNotReceive()
            .TraceAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that an unknown destination does not invoke connectors.
    /// </summary>
    [TestMethod]
    public async Task UnknownDestination_ThrowsWithoutInvokingConnector()
    {
        // Arrange
        IPingTraceConnector connector = CreateConnector("ApiService");
        DefaultPingTrace sut = new(new[] { connector });

        // Act
        Func<Task> act = () => sut.TraceAsync("Missing", TestContext.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<PingTrace.Core.TraceRoutingException>();
        await connector.DidNotReceive()
            .TraceAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies rejection of empty destination names.
    /// </summary>
    /// <param name="destination">The invalid destination name.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    public async Task EmptyDestination_Throws(string destination)
    {
        // Arrange
        DefaultPingTrace sut = new();

        // Act
        Func<Task> act = () => sut.TraceAsync(destination, TestContext.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Verifies that a connector failure reaches the caller unchanged.
    /// </summary>
    [TestMethod]
    public async Task ConnectorFailure_Propagates()
    {
        // Arrange
        InvalidOperationException failure = new("Dependency failed.");
        IPingTraceConnector connector = CreateConnector("ApiService");

        connector.TraceAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<TraceResult>>(failure));

        DefaultPingTrace sut = new(new[] { connector });

        // Act
        Func<Task> act = () =>
            sut.TraceAsync("ApiService", TestContext.CancellationToken);

        // Assert
        var assertion =
            await act.Should().ThrowAsync<InvalidOperationException>();

        assertion.Which.Should().BeSameAs(failure);
    }

    /// <summary>
    /// Verifies that already-cancelled requests do not invoke dependencies.
    /// </summary>
    /// <param name="useDestination">
    /// Whether to exercise the destination overload.
    /// </param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancelledTrace_DoesNotInvokeConnector(bool useDestination)
    {
        // Arrange
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.CancellationToken);

        cancellation.Cancel();

        IPingTraceConnector connector = CreateConnector("ApiService");
        DefaultPingTrace sut = new(new[] { connector });

        // Act
        Func<Task> act = async () =>
        {
            if (useDestination)
            {
                await sut.TraceAsync("ApiService", cancellation.Token);
            }
            else
            {
                await sut.TraceAsync(cancellation.Token);
            }
        };

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        await connector.DidNotReceive()
            .TraceAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that tracing waits for the dependency to complete.
    /// </summary>
    /// <remarks>
    /// Explicit completion avoids unreliable elapsed-time assertions.
    /// </remarks>
    [TestMethod]
    public async Task DestinationTrace_AwaitsDependencyCompletion()
    {
        // Arrange
        TaskCompletionSource<IReadOnlyList<TraceResult>> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        IPingTraceConnector connector = CreateConnector("ApiService");
        connector.TraceAsync(Arg.Any<CancellationToken>())
            .Returns(completion.Task);

        DefaultPingTrace sut = new(new[] { connector });
        TraceResult remote = CreateResult("RemoteApi");

        // Act
        Task<IReadOnlyList<TraceResult>> pending = sut.TraceAsync("ApiService", TestContext.CancellationToken);

        bool completedBeforeDependency = pending.IsCompleted;
        completion.SetResult(new[] { remote });

        IReadOnlyList<TraceResult> results = await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);

        // Assert
        completedBeforeDependency.Should().BeFalse();
        results.Should().HaveCount(2);
        results[1].Should().BeSameAs(remote);
        results[0].FinishedAt.Should().NotBe(default);
    }

    /// <summary>
    /// Verifies rejection of ambiguous destination names.
    /// </summary>
    [TestMethod]
    public void DuplicateDestinationNames_AreRejectedIgnoringCase()
    {
        // Arrange
        IPingTraceConnector first = CreateConnector("ApiService");
        IPingTraceConnector second = CreateConnector("APISERVICE");

        // Act
        Action act = () =>
        {
            _ = new DefaultPingTrace(new[] { first, second });
        };

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Verifies that a dependency cannot shadow the local application.
    /// </summary>
    [TestMethod]
    public void DestinationMatchingApplicationName_IsRejected()
    {
        // Arrange
        IPingTraceConnector connector = CreateConnector(ApplicationName.ToUpperInvariant());

        // Act
        Action act = () =>
        {
            _ = new DefaultPingTrace(new[] { connector });
        };

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    private static IPingTraceConnector CreateConnector(string name)
    {
        IPingTraceConnector connector =
            Substitute.For<IPingTraceConnector>();

        connector.Name.Returns(name);
        return connector;
    }

    private static TraceResult CreateResult(string name)
    {
        TraceResult result = new() { Name = name };
        result.Finish("Remote payload");
        return result;
    }
}