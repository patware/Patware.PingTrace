using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NSubstitute;

using PingTrace.Core;
using PingTrace.Runtime;
using PingTrace.Tests;

using StackExchange.Redis;

namespace PingTrace.Adapter.RedisCache.Tests;

/// <summary>
/// Exercises Redis destinations through public registration without a Redis server.
/// </summary>
[TestClass]
public sealed class RedisTraceConnectorTests
{
    /// <summary>Gets or sets the current test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Verifies successful ping results and application-owned connection lifetime.</summary>
    [TestMethod]
    public async Task SuccessfulPing_ReturnsNamedResultWithoutDisposingConnection()
    {
        // Arrange
        using CapturingLoggerProvider logs = new();
        IDatabase database = Substitute.For<IDatabase>();
        database.PingAsync(Arg.Any<CommandFlags>()).Returns(Task.FromResult(TimeSpan.FromMilliseconds(1)));
        IConnectionMultiplexer connection = Substitute.For<IConnectionMultiplexer>();
        connection.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);
        using ServiceProvider provider = CreateProvider(connection, logs);
        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        IReadOnlyList<TraceResult> results = await sut.TraceAsync("Cache", TestContext.CancellationToken);

        // Assert
        results.Should().HaveCount(2);
        results[1].Name.Should().Be("Cache");
        results[1].Status.Should().Be(TraceStatus.Succeeded);
        results[1].FailureKind.Should().Be(TraceFailureKind.None);
        results[1].Payload.Should().Be("PONG");
        results[1].Message.Should().BeEmpty();
        results[1].ErrorCode.Should().BeEmpty();
        logs.Warnings.Should().BeEmpty();
        connection.DidNotReceive().Dispose();
        await database.Received(1).PingAsync(Arg.Any<CommandFlags>());
    }

    /// <summary>Verifies typed Redis failure classification and original exception logging.</summary>
    /// <param name="scenario">The simulated failure.</param>
    /// <param name="kind">The expected classification.</param>
    /// <param name="code">The expected stable diagnostic code.</param>
    [TestMethod]
    [DataRow("client-timeout", TraceFailureKind.Timeout, "Redis.ClientTimeout")]
    [DataRow("timeout", TraceFailureKind.Timeout, "Redis.DiagnosticTimeout")]
    [DataRow("authentication", TraceFailureKind.Authentication, "Redis.Connection.AuthenticationFailure")]
    [DataRow("connection", TraceFailureKind.Connection, "Redis.Connection.SocketFailure")]
    [DataRow("server", TraceFailureKind.RemoteError, "Redis.ServerError")]
    public async Task PingFailure_ReturnsResultAndLogsOriginalException(
        string scenario, TraceFailureKind kind, string code)
    {
        // Arrange
        using CapturingLoggerProvider logs = new();
        Exception exception = scenario switch
        {
            "client-timeout" => new RedisTimeoutException(CommandFlags.CommandRetryNever, "Internal details", CommandStatus.Unknown),
            "timeout" => new TimeoutException("Internal details"),
            "authentication" => new RedisConnectionException(ConnectionFailureType.AuthenticationFailure, CommandFlags.CommandRetryNever, "Internal details"),
            "connection" => new RedisConnectionException(ConnectionFailureType.SocketFailure, CommandFlags.CommandRetryNever, "Internal details"),
            _ => new RedisServerException(RedisErrorKind.Unknown, CommandFlags.CommandRetryNever, "Internal details")
        };
        IDatabase database = Substitute.For<IDatabase>();
        database.PingAsync(Arg.Any<CommandFlags>()).Returns(Task.FromException<TimeSpan>(exception));
        IConnectionMultiplexer connection = Substitute.For<IConnectionMultiplexer>();
        connection.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);
        using ServiceProvider provider = CreateProvider(connection, logs);
        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        IReadOnlyList<TraceResult> results = await sut.TraceAsync("Cache", TestContext.CancellationToken);

        // Assert
        results[1].Status.Should().Be(TraceStatus.Failed);
        results[1].FailureKind.Should().Be(kind);
        results[1].ErrorCode.Should().Be(code);
        results[1].Message.Should().NotBeNullOrWhiteSpace().And.NotContain("Internal details");
        results[1].Payload.Should().BeEmpty();
        results[1].FinishedAt.Should().NotBe(default);
        logs.Warnings.Should().ContainSingle();
        logs.Warnings.Single().Exception.Should().BeSameAs(exception);
        logs.Warnings.Single().Message.Should().Contain(code).And.Contain("Cache");
    }

    /// <summary>Verifies that cancellation interrupts waiting without recording a destination failure.</summary>
    [TestMethod]
    public async Task CancellationDuringPing_PropagatesWithoutWarning()
    {
        // Arrange
        using CapturingLoggerProvider logs = new();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        TaskCompletionSource<TimeSpan> ping = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IDatabase database = Substitute.For<IDatabase>();
        database.PingAsync(Arg.Any<CommandFlags>()).Returns(ping.Task);
        IConnectionMultiplexer connection = Substitute.For<IConnectionMultiplexer>();
        connection.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);
        using ServiceProvider provider = CreateProvider(connection, logs);
        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        Task<IReadOnlyList<TraceResult>> pending = sut.TraceAsync("Cache", cancellation.Token);
        cancellation.Cancel();
        Func<Task> act = () => pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);

        // Assert
        try
        {
            await act.Should().ThrowAsync<OperationCanceledException>();
            logs.Warnings.Should().BeEmpty();
            ping.Task.IsCompleted.Should().BeFalse();
        }
        finally
        {
            ping.TrySetResult(TimeSpan.Zero);
        }
    }

    /// <summary>Verifies deferred resolution and propagation of a missing DI registration.</summary>
    [TestMethod]
    public async Task MissingConnection_DoesNotBreakLocalTraceButFailsDestinationResolution()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddPingTrace(builder => builder.AddRedisCacheDestination("Cache"));
        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        TraceResult local = await sut.TraceAsync(TestContext.CancellationToken);
        Func<Task> act = () => sut.TraceAsync("Cache", TestContext.CancellationToken);

        // Assert
        local.Status.Should().Be(TraceStatus.Succeeded);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static ServiceProvider CreateProvider(IConnectionMultiplexer connection, CapturingLoggerProvider logs)
    {
        ServiceCollection services = new();
        services.AddSingleton(connection);
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddPingTrace(builder => builder.AddRedisCacheDestination("Cache"));
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }
}
