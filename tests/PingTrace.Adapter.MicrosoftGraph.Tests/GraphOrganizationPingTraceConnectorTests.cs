using Microsoft.Extensions.Logging;
using PingTrace.Tests;
using System.Net;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;

using PingTrace.Core;
using PingTrace.Runtime;

namespace PingTrace.Adapter.MicrosoftGraph.Tests;

/// <summary>
/// Verifies Graph organization destinations through public registration.
/// </summary>
/// <remarks>
/// Tests use the real Graph SDK and PingTrace connector with an in-memory
/// HTTP handler. This exercises request construction, deserialization,
/// result mapping, and failure handling without tenant access.
///
/// Anonymous authentication is confined to these tests. They do not
/// validate token acquisition, tenant permissions, or live connectivity.
/// </remarks>
[TestClass]
public sealed class GraphOrganizationPingTraceConnectorTests
{
    private const string Identity = "tenant:test-tenant; app:test-app";

    private const string OrganizationJson = """
        {
          "value": [
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "displayName": "Contoso",
              "verifiedDomains": [
                {
                  "name": "contoso.example",
                  "isDefault": true
                }
              ]
            }
          ]
        }
        """;

    /// <summary>
    /// Gets or sets the context supplied by MSTest.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Verifies the organization request and resulting diagnostic values.
    /// </summary>
    [TestMethod]
    public async Task SuccessfulRead_ReturnsOrganizationDiagnostics()
    {
        // Arrange
        int calls = 0;
        Uri? observedUri = null;
        HttpMethod? observedMethod = null;

        using ServiceProvider provider = CreateProvider((request, _) =>
        {
            calls++;
            observedUri = request.RequestUri;
            observedMethod = request.Method;

            return Task.FromResult(JsonResponse(OrganizationJson));
        });

        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        IReadOnlyList<TraceResult> results = await sut.TraceAsync("MICROSOFTGRAPH", TestContext.CancellationToken);

        // Assert
        calls.Should().Be(1);
        observedMethod.Should().Be(HttpMethod.Get);
        observedUri.Should().NotBeNull();
        observedUri!.Host.Should().Be("graph.test");
        observedUri.AbsolutePath.Should().Be("/v1.0/organization");

        Uri.UnescapeDataString(observedUri.Query)
            .Should().Contain("$select=id,displayName,verifiedDomains");

        results.Should().HaveCount(2);

        TraceResult dependency = results[1];
        dependency.Status.Should().Be(TraceStatus.Succeeded);
        dependency.Name.Should().Be("MicrosoftGraph");
        dependency.MachineName.Should().Be("graph.test");
        dependency.Identity.Should().Be(Identity);
        dependency.StartedAt.Should().NotBe(default);
        dependency.FinishedAt.Should().NotBe(default);

        using JsonDocument payload = JsonDocument.Parse(dependency.Payload);
        JsonElement root = payload.RootElement;

        root.GetProperty("TenantId").GetString()
            .Should().Be("11111111-1111-1111-1111-111111111111");

        root.GetProperty("DisplayName").GetString()
            .Should().Be("Contoso");

        root.GetProperty("VerifiedDomains")[0].GetString()
            .Should().Be("contoso.example");

        root.GetProperty("IdentitySource").GetString()
            .Should().Be("ConfiguredAuthentication");
    }

    /// <summary>
    /// Verifies that local operations do not resolve the Graph client.
    /// </summary>
    [TestMethod]
    public async Task LocalOperations_DoNotResolveClient()
    {
        // Arrange
        ServiceCollection services = new();

        // Deliberately omit GraphServiceClient registration.
        services.AddPingTrace(builder =>
        {
            builder.AddGraphOrganizationDestination(
                "MicrosoftGraph",
                Identity);
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
        local.Payload.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies that unsuccessful responses preserve Graph error details.
    /// </summary>
    /// <param name="statusCode">The simulated HTTP failure status.</param>
    [TestMethod]
    [DataRow(401)]
    [DataRow(403)]
    [DataRow(503)]
    public async Task FailedRead_ReturnsStructuredFailure(int statusCode)
    {
        // Arrange
        using CapturingLoggerProvider logs = new();
        const string errorJson = """
            {
              "error": {
                "code": "SimulatedFailure",
                "message": "The simulated request failed."
              }
            }
            """;

        using ServiceProvider provider = CreateProvider((_, _) =>
            Task.FromResult(JsonResponse(
                errorJson,
                (HttpStatusCode)statusCode)), logs);

        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        IReadOnlyList<TraceResult> results = await sut.TraceAsync(
            "MicrosoftGraph",
            TestContext.CancellationToken);

        // Assert
        results.Should().HaveCount(2);
        TraceResult failure = results[1];
        failure.Status.Should().Be(TraceStatus.Failed);
        failure.FailureKind.Should().Be(statusCode == 401
            ? TraceFailureKind.Authentication
            : statusCode == 403 ? TraceFailureKind.Authorization : TraceFailureKind.RemoteError);
        failure.ErrorCode.Should().Be($"Graph.Http.{statusCode}");
        failure.Message.Should().Be($"The Graph organization request returned HTTP {statusCode}.");
        failure.Payload.Should().BeEmpty();
        logs.Warnings.Should().ContainSingle();
        var error = logs.Warnings.Single().Exception.Should()
            .BeOfType<Microsoft.Graph.Models.ODataErrors.ODataError>().Subject;
        error.ResponseStatusCode.Should().Be(statusCode);
        error.Error!.Code.Should().Be("SimulatedFailure");
    }

    /// <summary>
    /// Verifies rejection of missing, ambiguous, or unidentified organizations.
    /// </summary>
    /// <param name="json">The simulated response body.</param>
    [TestMethod]
    [DataRow("{}")]
    [DataRow("""{"value":[]}""")]
    [DataRow("""{"value":[{}]}""")]
    [DataRow("""{"value":[{"id":" "}]}""")]
    [DataRow("""{"value":[{"id":"one"},{"id":"two"}]}""")]
    public async Task InvalidOrganizationResponse_IsRejected(string json)
    {
        // Arrange
        using ServiceProvider provider = CreateProvider((_, _) =>
            Task.FromResult(JsonResponse(json)));

        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        IReadOnlyList<TraceResult> results = await sut.TraceAsync(
            "MicrosoftGraph",
            TestContext.CancellationToken);

        // Assert
        results[1].Status.Should().Be(TraceStatus.Failed);
        results[1].FailureKind.Should().Be(TraceFailureKind.InvalidResponse);
        results[1].Message.Should().NotBeNullOrWhiteSpace();
        results[1].ErrorCode.Should().BeOneOf("Graph.InvalidOrganizationCount", "Graph.InvalidOrganizationId");
    }

    /// <summary>
    /// Verifies that malformed JSON becomes an invalid-response diagnostic result.
    /// </summary>
    [TestMethod]
    public async Task MalformedJson_IsRejected()
    {
        // Arrange
        using ServiceProvider provider = CreateProvider((_, _) =>
            Task.FromResult(JsonResponse("not-json")));

        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        IReadOnlyList<TraceResult> results = await sut.TraceAsync(
            "MicrosoftGraph",
            TestContext.CancellationToken);

        // Assert
        results[1].Status.Should().Be(TraceStatus.Failed);
        results[1].FailureKind.Should().Be(TraceFailureKind.InvalidResponse);
        results[1].Message.Should().NotBeNullOrWhiteSpace();
        results[1].ErrorCode.Should().Be("Graph.InvalidJson");
    }

    /// <summary>
    /// Verifies cancellation of a request already executing.
    /// </summary>
    /// <remarks>
    /// The handler signals when execution begins so cancellation cannot
    /// accidentally occur before the request reaches the transport.
    ///
    /// HttpClient can create a linked token. Observing cancellation
    /// behavior is therefore preferable to asserting token equality.
    /// </remarks>
    [TestMethod]
    public async Task CancellationDuringRead_ReachesTransport()
    {
        // Arrange
        TaskCompletionSource<bool> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        CancellationToken observedToken = default;

        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);

        using ServiceProvider provider = CreateProvider(async (_, token) =>
        {
            observedToken = token;
            entered.TrySetResult(true);

            await Task.Delay(Timeout.InfiniteTimeSpan, token);

            return JsonResponse(OrganizationJson);
        });

        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        Task<IReadOnlyList<TraceResult>> pending = sut.TraceAsync("MicrosoftGraph", cancellation.Token);

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);
        }
        finally
        {
            cancellation.Cancel();
        }

        Func<Task> act = async () =>
        {
            await pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);
        };

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        observedToken.IsCancellationRequested.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that tracing leaves the application's client usable.
    /// </summary>
    [TestMethod]
    public async Task Trace_DoesNotDisposeApplicationClient()
    {
        // Arrange
        int calls = 0;

        using ServiceProvider provider = CreateProvider((_, _) =>
        {
            calls++;
            return Task.FromResult(JsonResponse(OrganizationJson));
        });

        using IServiceScope scope = provider.CreateScope();
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();
        GraphServiceClient client = scope.ServiceProvider.GetRequiredService<GraphServiceClient>();

        // Act
        await sut.TraceAsync("MicrosoftGraph", TestContext.CancellationToken);

        var response = await client.Organization.GetAsync(cancellationToken: TestContext.CancellationToken);

        // Assert
        calls.Should().Be(2);
        response!.Value.Should().HaveCount(1);
    }

    /// <summary>Verifies invalid client configuration fails before issuing an HTTP request.</summary>
    [TestMethod]
    public async Task InvalidBaseUrl_ReturnsConfigurationFailureWithoutRequest()
    {
        // Arrange
        using CapturingLoggerProvider logs = new();
        int calls = 0;
        using ServiceProvider provider = CreateProvider((_, _) =>
        {
            calls++;
            return Task.FromResult(JsonResponse(OrganizationJson));
        }, logs);
        using IServiceScope scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<GraphServiceClient>().RequestAdapter.BaseUrl = "relative-path";
        IPingTrace sut = scope.ServiceProvider.GetRequiredService<IPingTrace>();

        // Act
        IReadOnlyList<TraceResult> results = await sut.TraceAsync("MicrosoftGraph", TestContext.CancellationToken);

        // Assert
        calls.Should().Be(0);
        results[1].Status.Should().Be(TraceStatus.Failed);
        results[1].FailureKind.Should().Be(TraceFailureKind.Configuration);
        results[1].ErrorCode.Should().Be("Graph.InvalidBaseUrl");
        logs.Warnings.Should().ContainSingle();
        logs.Warnings.Single().Exception.Should().BeNull();
    }

    private static ServiceProvider CreateProvider(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        CapturingLoggerProvider? logs = null)
    {
        ServiceCollection services = new();
        if (logs is not null)
        {
            services.AddLogging(builder => builder.AddProvider(logs));
        }

        services.AddSingleton<System.Net.Http.HttpClient>(_ => new System.Net.Http.HttpClient(new StubHandler(send)));

        services.AddSingleton<GraphServiceClient>(provider =>
            new GraphServiceClient(
                provider.GetRequiredService<System.Net.Http.HttpClient>(),
                new AnonymousAuthenticationProvider(),
                "https://graph.test/v1.0"));

        services.AddPingTrace(builder =>
        {
            builder.AddGraphOrganizationDestination("MicrosoftGraph", Identity);
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

    private static HttpResponseMessage JsonResponse(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return send(request, cancellationToken);
        }
    }
}
