using Microsoft.Extensions.Logging;
using PingTrace.Tests;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using PingTrace.Core;
using PingTrace.Runtime;

namespace PingTrace.Adapter.HttpClient.Tests;

/// <summary>
/// Verifies HTTP destinations through their public registration API.
/// </summary>
/// <remarks>
/// In-memory handlers exercise client configuration and deserialization
/// without depending on a running server.
/// </remarks>
[TestClass]
public sealed class HttpPingTraceConnectorTests
{

    /// <summary>
    /// Gets or sets the context supplied by MSTest.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Verifies independent HTTP clients and preservation of remote names.
    /// </summary>
    [TestMethod]
    public async Task MultipleDestinations_UseTheirOwnClients()
    {
        // Arrange
        ServiceCollection services = new();
        List<string> requests = new();

        services.AddPingTrace(builder =>
        {
            builder.AddHttpDestination("First", client =>
            {
                client.BaseAddress = new Uri("https://first.test/");
            }).ConfigurePrimaryHttpMessageHandler(() =>
                new StubHandler((request, _) =>
                {
                    requests.Add(request.RequestUri!.AbsoluteUri);
                    request.Method.Should().Be(HttpMethod.Get);
                    return Task.FromResult(JsonResponse(
                        """{"name":"FirstRemote","payload":"one"}"""));
                }));

            builder.AddHttpDestination("Second", client =>
            {
                client.BaseAddress = new Uri("https://second.test/");
            }).ConfigurePrimaryHttpMessageHandler(() =>
                new StubHandler((request, _) =>
                {
                    requests.Add(request.RequestUri!.AbsoluteUri);
                    return Task.FromResult(JsonResponse(
                        """{"name":"SecondRemote","payload":"two"}"""));
                }));
        });

        using ServiceProvider provider = BuildProvider(services);
        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        IReadOnlyList<TraceResult> first = await sut.TraceAsync("FIRST", TestContext.CancellationToken);
        IReadOnlyList<TraceResult> second = await sut.TraceAsync("Second", TestContext.CancellationToken);

        // Assert
        requests.Should().Equal(
            "https://first.test/pingtrace/Trace",
            "https://second.test/pingtrace/Trace");

        first.Should().HaveCount(2);
        first[1].Name.Should().Be("FirstRemote");
        first[1].Payload.Should().Be("one");
        first[1].Status.Should().Be(TraceStatus.Unknown);

        second.Should().HaveCount(2);
        second[1].Name.Should().Be("SecondRemote");
        second[1].Payload.Should().Be("two");
    }

    /// <summary>
    /// Verifies that Ping and local Trace send no HTTP requests.
    /// </summary>
    [TestMethod]
    public async Task LocalOperations_DoNotSendHttpRequests()
    {
        // Arrange
        int requestCount = 0;
        using ServiceProvider provider = CreateProvider((_, _) =>
        {
            requestCount++;
            return Task.FromResult(JsonResponse("""{"name":"Remote"}"""));
        });

        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        string ping = sut.Ping();
        TraceResult local = await sut.TraceAsync(TestContext.CancellationToken);

        // Assert
        ping.Should().Be("Pong");
        local.Name.Should().NotBeNullOrWhiteSpace();
        requestCount.Should().Be(0);
    }

    /// <summary>
    /// Verifies server-root and base-path-relative endpoint resolution.
    /// </summary>
    /// <param name="path">The configured trace path.</param>
    /// <param name="expectedUri">The expected outgoing URI.</param>
    [TestMethod]
    [DataRow("/pingtrace/Trace", "https://remote.test/pingtrace/Trace")]
    [DataRow("diagnostics/Trace", "https://remote.test/application/diagnostics/Trace")]
    public async Task TracePath_ResolvesAgainstBaseAddress(
        string path,
        string expectedUri)
    {
        // Arrange
        Uri? observedUri = null;
        using ServiceProvider provider = CreateProvider(
            (request, _) =>
            {
                observedUri = request.RequestUri;
                return Task.FromResult(JsonResponse("""{"name":"Remote"}"""));
            },
            path);

        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        await sut.TraceAsync("ApiService", TestContext.CancellationToken);

        // Assert
        observedUri.Should().Be(new Uri(expectedUri));
    }

    /// <summary>
    /// Verifies that HTTP error responses become diagnostic results.
    /// </summary>
    /// <param name="statusCode">The remote failure status.</param>
    /// <param name="recursive">Whether to invoke recursive tracing.</param>
    [TestMethod]
    [DataRow(401, false)]
    [DataRow(401, true)]
    [DataRow(403, false)]
    [DataRow(403, true)]
    [DataRow(404, false)]
    [DataRow(404, true)]
    [DataRow(503, false)]
    [DataRow(503, true)]
    public async Task UnsuccessfulResponse_ReturnsDiagnosticResult(
        int statusCode,
        bool recursive)
    {
        // Arrange
        using ServiceProvider provider = CreateProvider(
            (_, _) => Task.FromResult(
                new HttpResponseMessage((HttpStatusCode)statusCode)));

        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        IReadOnlyList<TraceResult> results = recursive
            ? await sut.TraceAllAsync(
                cancellationToken: TestContext.CancellationToken)
            : await sut.TraceAsync(
                "ApiService",
                TestContext.CancellationToken);

        // Assert
        AssertFailureResult(results, $"The destination returned HTTP {statusCode} ({(HttpStatusCode)statusCode}).",
            statusCode == 401 ? TraceFailureKind.Authentication :
            statusCode == 403 ? TraceFailureKind.Authorization : TraceFailureKind.RemoteError,
            $"Http.Status.{statusCode}");
    }

    /// <summary>
    /// Verifies that transport failures become diagnostic results without
    /// exposing the original exception message.
    /// </summary>
    /// <param name="recursive">Whether to invoke recursive tracing.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TransportFailure_ReturnsDiagnosticResult(bool recursive)
    {
        // Arrange
        using CapturingLoggerProvider logs = new();
        HttpRequestException failure = new("Internal transport details.");
        using ServiceProvider provider = CreateProvider(
            (_, _) => Task.FromException<HttpResponseMessage>(
                failure), logs: logs);

        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        IReadOnlyList<TraceResult> results = recursive
            ? await sut.TraceAllAsync(
                cancellationToken: TestContext.CancellationToken)
            : await sut.TraceAsync(
                "ApiService",
                TestContext.CancellationToken);

        // Assert
        AssertFailureResult(
            results,
            "HTTP communication failed (Unknown).",
            TraceFailureKind.Connection, "Http.Transport.Unknown");
        logs.Warnings.Should().ContainSingle();
        logs.Warnings.Single().Exception.Should().BeSameAs(failure);
        results[1].Message.Should().NotContain(failure.Message);
    }

    /// <summary>
    /// Verifies that HttpClient timeouts become diagnostic results.
    /// </summary>
    /// <param name="recursive">Whether to invoke recursive tracing.</param>
    /// <remarks>
    /// The handler waits for cancellation. HttpClient supplies the timeout;
    /// the test does not manufacture a timeout exception or assert an exact duration.
    /// A separate cancellation limit prevents an indefinite test run.
    /// </remarks>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ClientTimeout_ReturnsDiagnosticResult(bool recursive)
    {
        // Arrange
        using CancellationTokenSource guard =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.CancellationToken);

        guard.CancelAfter(TimeSpan.FromSeconds(10));

        using ServiceProvider provider = CreateProvider(
            async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return JsonResponse("""{"name":"Remote"}""");
            },
            timeout: TimeSpan.FromMilliseconds(200));

        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        IReadOnlyList<TraceResult> results = recursive
            ? await sut.TraceAllAsync(cancellationToken: guard.Token)
            : await sut.TraceAsync("ApiService", guard.Token);

        // Assert
        guard.IsCancellationRequested.Should().BeFalse();

        AssertFailureResult(results, "The HTTP client timeout expired before diagnostics completed.",
            TraceFailureKind.Timeout, "Http.ClientTimeout");
    }


    /// <summary>
    /// Verifies that caller cancellation takes precedence over a transport failure.
    /// </summary>
    /// <param name="recursive">Whether to invoke recursive tracing.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationAlongsideTransportFailure_Propagates(
        bool recursive)
    {
        // Arrange
        using CapturingLoggerProvider logs = new();
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.CancellationToken);

        using ServiceProvider provider = CreateProvider((_, _) =>
        {
            cancellation.Cancel();

            return Task.FromException<HttpResponseMessage>(
                new HttpRequestException("Transport failed during cancellation."));
        }, logs: logs);

        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        Func<Task> act = () => recursive
            ? sut.TraceAllAsync(cancellationToken: cancellation.Token)
            : sut.TraceAsync("ApiService", cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        logs.Warnings.Should().BeEmpty();
    }


    /// <summary>
    /// Verifies that an unavailable HTTP destination does not prevent
    /// subsequent destinations from being traced.
    /// </summary>
    /// <remarks>
    /// Destination names establish traversal order. Request recording proves
    /// that the second destination was actually invoked.
    /// </remarks>
    [TestMethod]
    public async Task TraceAll_UnreachableDestination_ContinuesTraversal()
    {
        // Arrange
        ServiceCollection services = new();
        List<string> requests = [];

        services.AddPingTrace(builder =>
        {
            builder.AddHttpDestination("A-Unavailable", client =>
            {
                client.BaseAddress = new Uri("https://unavailable.test/");
            }).ConfigurePrimaryHttpMessageHandler(() =>
                new StubHandler((request, _) =>
                {
                    request.Method.Should().Be(HttpMethod.Post);
                    requests.Add("A-Unavailable");

                    return Task.FromException<HttpResponseMessage>(
                        new HttpRequestException("Connection refused."));
                }));

            builder.AddHttpDestination("B-Available", client =>
            {
                client.BaseAddress = new Uri("https://available.test/");
            }).ConfigurePrimaryHttpMessageHandler(() =>
                new StubHandler((request, _) =>
                {
                    request.Method.Should().Be(HttpMethod.Post);
                    requests.Add("B-Available");

                    return Task.FromResult(JsonResponse(
                        """[{"name":"RemoteApi","payload":"healthy"}]"""));
                }));
        });

        using ServiceProvider provider = BuildProvider(services);
        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        IReadOnlyList<TraceResult> results = await sut.TraceAllAsync(
            cancellationToken: TestContext.CancellationToken);

        // Assert
        requests.Should().Equal("A-Unavailable", "B-Available");

        results.Should().HaveCount(3);
        results[0].FinishedAt.Should().NotBe(default);

        results[1].Status.Should().Be(TraceStatus.Failed);
        results[1].FailureKind.Should().Be(TraceFailureKind.Connection);
        results[1].ErrorCode.Should().Be("Http.Transport.Unknown");
        results[1].Payload.Should().BeEmpty();
        results[0].Status.Should().Be(TraceStatus.Succeeded);
        results[1].Name.Should().Be("A-Unavailable");
        results[1].MachineName.Should().Be("unavailable.test");
        results[1].Identity.Should().BeEmpty();
        results[1].Message.Should().Be(
            "HTTP communication failed (Unknown).");
        results[1].FinishedAt.Should().NotBe(default);

        results[2].Name.Should().Be("RemoteApi");
        results[2].Payload.Should().Be("healthy");
    }

    /// <summary>
    /// Verifies that malformed JSON is rejected by both trace operations.
    /// </summary>
    /// <param name="recursive">Whether to invoke recursive tracing.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MalformedJson_IsRejected(bool recursive)
    {
        // Arrange
        using ServiceProvider provider = CreateProvider(
            (_, _) => Task.FromResult(JsonResponse("not-json")));

        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        IReadOnlyList<TraceResult> results = recursive
            ? await sut.TraceAllAsync(cancellationToken: TestContext.CancellationToken)
            : await sut.TraceAsync("ApiService", TestContext.CancellationToken);

        // Assert
        results.Should().HaveCount(2);
        results[1].Status.Should().Be(TraceStatus.Failed);
        results[1].FailureKind.Should().Be(TraceFailureKind.InvalidResponse);
        results[1].ErrorCode.Should().Be("Http.InvalidDiagnostics");
        results[1].Message.Should().NotBeNullOrWhiteSpace();
        results[1].Payload.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies cancellation of an HTTP request already executing.
    /// </summary>
    /// <param name="recursive">Whether to invoke recursive tracing.</param>
    /// <remarks>
    /// The handler signals when execution begins. Cancellation therefore
    /// exercises the active transport rather than only an entry-point guard.
    /// </remarks>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Cancellation_ReachesExecutingHttpRequest(bool recursive)
    {
        // Arrange
        TaskCompletionSource<bool> entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.CancellationToken);

        using ServiceProvider provider = CreateProvider(async (_, token) =>
        {
            entered.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);

            return JsonResponse(
                recursive
                    ? """[{"name":"Remote"}]"""
                    : """{"name":"Remote"}""");
        });

        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        Task<IReadOnlyList<TraceResult>> pending = recursive
            ? sut.TraceAllAsync(cancellationToken: cancellation.Token)
            : sut.TraceAsync("ApiService", cancellation.Token);

        try
        {
            await entered.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.CancellationToken);
        }
        finally
        {
            cancellation.Cancel();
        }

        Func<Task> act = async () =>
        {
            await pending.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.CancellationToken);
        };

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Verifies that a missing base address fails before sending a request.
    /// </summary>
    /// <param name="recursive">Whether to invoke recursive tracing.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MissingBaseAddress_IsRejectedBeforeSending(bool recursive)
    {
        // Arrange
        ServiceCollection services = new();
        int requestCount = 0;

        services.AddPingTrace(builder =>
        {
            builder.AddHttpDestination("ApiService", _ => { })
                .ConfigurePrimaryHttpMessageHandler(() =>
                    new StubHandler((_, _) =>
                    {
                        requestCount++;

                        return Task.FromResult(JsonResponse(
                            recursive
                                ? """[{"name":"Remote"}]"""
                                : """{"name":"Remote"}"""));
                    }));
        });

        using ServiceProvider provider = BuildProvider(services);
        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        IReadOnlyList<TraceResult> results = recursive
            ? await sut.TraceAllAsync(cancellationToken: TestContext.CancellationToken)
            : await sut.TraceAsync("ApiService", TestContext.CancellationToken);

        // Assert
        results.Should().HaveCount(2);
        results[1].Status.Should().Be(TraceStatus.Failed);
        results[1].FailureKind.Should().Be(TraceFailureKind.Configuration);
        results[1].ErrorCode.Should().Be("Http.MissingBaseAddress");
        results[1].Message.Should().NotBeNullOrWhiteSpace();
        results[1].Payload.Should().BeEmpty();
        requestCount.Should().Be(0);
    }

    /// <summary>
    /// Verifies rejection of invalid local and recursive endpoint paths.
    /// </summary>
    /// <param name="path">The invalid endpoint path.</param>
    /// <param name="recursive">Whether to validate the recursive path.</param>
    /// <remarks>
    /// Both configurable paths must remain relative to the configured server.
    /// </remarks>
    [TestMethod]
    [DataRow("https://other.test/Trace", false)]
    [DataRow("https://other.test/Trace", true)]
    [DataRow("//other.test/Trace", false)]
    [DataRow("//other.test/Trace", true)]
    [DataRow(@"diagnostics\Trace", false)]
    [DataRow(@"diagnostics\Trace", true)]
    [DataRow("", false)]
    [DataRow("", true)]
    [DataRow(" ", false)]
    [DataRow(" ", true)]
    public void InvalidTracePath_IsRejected(string path, bool recursive)
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        Action act = () => services.AddPingTrace(builder =>
        {
            builder.AddHttpDestination(
                "ApiService",
                client =>
                {
                    client.BaseAddress = new Uri("https://remote.test/");
                },
                tracePath: recursive ? "/pingtrace/Trace" : path,
                traceAllPath: recursive ? path : "/pingtrace/TraceAll");
        });

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName(recursive ? "traceAllPath" : "tracePath");
    }

    /// <summary>
    /// Verifies the recursive HTTP method, path, request body, and response.
    /// </summary>
    /// <param name="path">The configured recursive endpoint path.</param>
    /// <param name="expectedUri">The expected destination URI.</param>
    /// <remarks>
    /// This exercises serialization through the public registration API.
    /// It detects accidentally calling the local Trace endpoint or dropping
    /// traversal state during transport.
    /// </remarks>
    [TestMethod]
    [DataRow("/pingtrace/TraceAll", "https://remote.test/pingtrace/TraceAll")]
    [DataRow("diagnostics/TraceAll", "https://remote.test/application/diagnostics/TraceAll")]
    public async Task TraceAll_PostsTraversalStateAndReadsSubtree(
        string path,
        string expectedUri)
    {
        // Arrange
        HttpMethod? observedMethod = null;
        Uri? observedUri = null;
        TraceAllRequest? observedRequest = null;

        using ServiceProvider provider = CreateProvider(
            async (message, token) =>
            {
                observedMethod = message.Method;
                observedUri = message.RequestUri;
                observedRequest =
                    await message.Content!.ReadFromJsonAsync<TraceAllRequest>(
                        cancellationToken: token);

                return JsonResponse(
                    """
                [
                  {"name":"RemoteApi","payload":"api"},
                  {"name":"Database","payload":"db"}
                ]
                """);
            },
            traceAllPath: path);

        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        Guid ancestor = Guid.NewGuid();
        TraceAllRequest request = new()
        {
            RemainingDepth = 3,
            Ancestors = new[] { ancestor }
        };

        Guid localNode = provider.GetRequiredService<TraceAllNode>().Id;

        // Act
        IReadOnlyList<TraceResult> results = await sut.TraceAllAsync(
            request,
            TestContext.CancellationToken);

        // Assert
        observedMethod.Should().Be(HttpMethod.Post);
        observedUri.Should().Be(new Uri(expectedUri));

        observedRequest.Should().NotBeNull();
        observedRequest!.RemainingDepth.Should().Be(2);
        observedRequest.Ancestors.Should().Equal(ancestor, localNode);
        observedRequest.DeadlineUtc.Should().Be(request.DeadlineUtc);

        results.Skip(1).Select(result => result.Name)
            .Should().Equal("RemoteApi", "Database");
        results.Skip(1).Select(result => result.Payload)
            .Should().Equal("api", "db");

        request.RemainingDepth.Should().Be(3);
        request.Ancestors.Should().Equal(ancestor);
    }

    /// <summary>
    /// Verifies rejection of empty or invalid recursive result collections.
    /// </summary>
    /// <param name="json">The invalid response body.</param>
    [TestMethod]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("[null]")]
    [DataRow("[{}]")]
    [DataRow("[{\"name\":\" \"}]")]
    [DataRow("[{\"name\":\"RemoteApi\"},null]")]
    public async Task TraceAll_InvalidSubtree_IsRejected(string json)
    {
        // Arrange
        using ServiceProvider provider = CreateProvider(
            (_, _) => Task.FromResult(JsonResponse(json)));

        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        IReadOnlyList<TraceResult> results = await sut.TraceAllAsync(
            cancellationToken: TestContext.CancellationToken);

        // Assert
        results.Should().HaveCount(2);
        results[1].Status.Should().Be(TraceStatus.Failed);
        results[1].FailureKind.Should().Be(TraceFailureKind.InvalidResponse);
        results[1].ErrorCode.Should().Be("Http.InvalidDiagnostics");
        results[1].Message.Should().NotBeNullOrWhiteSpace();
        results[1].Payload.Should().BeEmpty();
    }


    /// <summary>Verifies that HTTP success preserves a remote diagnostic failure and its evidence.</summary>
    /// <param name="recursive">Whether the response contains a recursive result array.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RemoteFailedResult_IsPreservedWithoutLocalWarning(bool recursive)
    {
        // Arrange
        using CapturingLoggerProvider logs = new();
        TraceResult remote = new() { Name = "Remote", MachineName = "dev-api", Payload = "partial evidence" };
        remote.Fail(TraceFailureKind.Authentication, "Remote.Auth", "Authentication failed.");
        string json = recursive
            ? JsonSerializer.Serialize(new[] { remote })
            : JsonSerializer.Serialize(remote);
        using ServiceProvider provider = CreateProvider(
            (_, _) => Task.FromResult(JsonResponse(json)), logs: logs);
        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        IReadOnlyList<TraceResult> results = recursive
            ? await sut.TraceAllAsync(cancellationToken: TestContext.CancellationToken)
            : await sut.TraceAsync("ApiService", TestContext.CancellationToken);

        // Assert
        results.Should().HaveCount(2);
        results[1].Should().BeEquivalentTo(remote);
        results[0].Status.Should().Be(TraceStatus.Succeeded);
        logs.Warnings.Should().BeEmpty();
    }

    private static ServiceProvider CreateProvider(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        string tracePath = "/pingtrace/Trace",
        string traceAllPath = "/pingtrace/TraceAll",
        TimeSpan? timeout = null,
        CapturingLoggerProvider? logs = null)
    {
        ServiceCollection services = new();
        if (logs is not null)
        {
            services.AddLogging(builder => builder.AddProvider(logs));
        }

        services.AddPingTrace(builder =>
        {
            builder.AddHttpDestination(
                "ApiService",
                client =>
                {
                    client.BaseAddress = new Uri("https://remote.test/application/");

                    if (timeout.HasValue)
                    {
                        client.Timeout = timeout.Value;
                    }
                },
                tracePath,
                traceAllPath)
                .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(send));
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

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static void AssertFailureResult(IReadOnlyList<TraceResult> results, string expectedMessage, TraceFailureKind expectedKind, string expectedCode)
    {
        results.Should().HaveCount(2);

        results[0].Name.Should().NotBeNullOrWhiteSpace();
        results[0].FinishedAt.Should().NotBe(default);

        TraceResult failure = results[1];

        failure.Name.Should().Be("ApiService");
        failure.MachineName.Should().Be("remote.test");
        failure.Identity.Should().BeEmpty();
        failure.Payload.Should().BeEmpty();
        failure.Message.Should().Be(expectedMessage);
        failure.Status.Should().Be(TraceStatus.Failed);
        failure.FailureKind.Should().Be(expectedKind);
        failure.ErrorCode.Should().Be(expectedCode);
        failure.StartedAt.Should().NotBe(default);
        failure.FinishedAt.Should().NotBe(default);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return send(request, cancellationToken);
        }
    }
}
