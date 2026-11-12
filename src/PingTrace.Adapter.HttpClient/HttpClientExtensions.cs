using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using PingTrace.Core;
using PingTrace.Runtime;

using NetHttpClient = System.Net.Http.HttpClient;

namespace PingTrace.Adapter.HttpClient;

/// <summary>
/// Registers HTTP destinations for PingTrace.
/// </summary>
public static class HttpClientExtensions
{
    /// <summary>
    /// Registers a named dependency reached through an HTTP client.
    /// </summary>
    /// <param name="builder">
    /// The PingTrace registration builder.
    /// </param>
    /// <param name="name">
    /// The destination name used by the calling application.
    /// </param>
    /// <param name="configureClient">
    /// Configures the client, including its base address.
    /// </param>
    /// <param name="tracePath">
    /// The relative path to the remote application's local Trace endpoint.
    /// </param>
    /// <param name="traceAllPath">
    /// The relative path to the remote application's recursive TraceAll endpoint.
    /// </param>
    /// <returns>
    /// The HTTP client builder for configuring handlers and other
    /// HTTP client services.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// The builder or client configuration callback is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The destination name or either trace path is empty or whitespace,
    /// or a trace path contains a backslash, begins with two forward slashes,
    /// or specifies an absolute URI.
    /// </exception>
    /// <remarks>
    /// Each registration receives a separate named HTTP client, allowing
    /// multiple HTTP destinations to use the same connector implementation.
    /// The default path begins at the server root. To preserve a path prefix
    /// in BaseAddress, supply a relative path without a leading slash and
    /// give BaseAddress a trailing slash.
    /// The local and recursive paths are independent; configure both when
    /// the remote application uses a custom prefix or base-address path.
    /// </remarks>
    public static IHttpClientBuilder AddHttpDestination(
        this PingTraceBuilder builder,
        string name,
        Action<NetHttpClient> configureClient,
        string tracePath = "/pingtrace/Trace",
        string traceAllPath = "/pingtrace/TraceAll")
    {
        return AddHttpDestination(
            builder: builder,
            name: name,
            configureClient: configureClient,
            tracePath: tracePath,
            traceAllPath: traceAllPath,
            discoverPath: "/pingtrace/Discover",
            traceRoutePath: "/pingtrace/TraceRoute");
    }

    /// <summary>
    /// Registers an HTTP destination with explicit trace and discovery paths.
    /// </summary>
    /// <param name="builder">The PingTrace registration builder.</param>
    /// <param name="name">The destination name.</param>
    /// <param name="configureClient">Configures the HTTP client.</param>
    /// <param name="tracePath">The local trace endpoint path.</param>
    /// <param name="traceAllPath">The recursive trace endpoint path.</param>
    /// <param name="discoverPath">The recursive discovery endpoint path.</param>
    /// <returns>The HTTP client builder for further configuration.</returns>
    /// <remarks>
    /// All endpoint arguments are required to avoid ambiguity with the
    /// overload that supplies default endpoint paths.
    /// Delegating to the full overload keeps validation and registration
    /// consistent and uses the default routed trace endpoint.
    /// </remarks>
    public static IHttpClientBuilder AddHttpDestination(
        this PingTraceBuilder builder,
        string name,
        Action<NetHttpClient> configureClient,
        string tracePath,
        string traceAllPath,
        string discoverPath)
    {
        return AddHttpDestination(
            builder: builder,
            name: name,
            configureClient: configureClient,
            tracePath: tracePath,
            traceAllPath: traceAllPath,
            discoverPath: discoverPath,
            traceRoutePath: "/pingtrace/TraceRoute");
    }

    /// <summary>
    /// Registers a named dependency reached through an HTTP client.
    /// </summary>
    /// <param name="builder">
    /// The PingTrace registration builder.
    /// </param>
    /// <param name="name">
    /// The destination name used by the calling application.
    /// </param>
    /// <param name="configureClient">
    /// Configures the client, including its base address.
    /// </param>
    /// <param name="tracePath">
    /// The relative path to the remote application's local Trace endpoint.
    /// </param>
    /// <param name="traceAllPath">
    /// The relative path to the remote application's recursive TraceAll endpoint.
    /// </param>
    /// <param name="discoverPath">The recursive discovery endpoint path.</param>
    /// <param name="traceRoutePath">
    /// The remote endpoint used to execute an explicit route.
    /// </param>
    /// <returns>
    /// The HTTP client builder for configuring handlers and other
    /// HTTP client services.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// The builder or client configuration callback is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The destination name or either trace path is empty or whitespace,
    /// or a trace path contains a backslash, begins with two forward slashes,
    /// or specifies an absolute URI.
    /// </exception>
    /// <remarks>
    /// Each registration receives a separate named HTTP client, allowing
    /// multiple HTTP destinations to use the same connector implementation.
    /// The default path begins at the server root. To preserve a path prefix
    /// in BaseAddress, supply a relative path without a leading slash and
    /// give BaseAddress a trailing slash.
    /// The local and recursive paths are independent; configure both when
    /// the remote application uses a custom prefix or base-address path.
    ///
    /// All endpoint paths are independently configurable so discovery and
    /// routed tracing work with custom endpoint prefixes.
    /// Existing overloads retain their signatures for compiled consumers.
    /// </remarks>

    public static IHttpClientBuilder AddHttpDestination(
        this PingTraceBuilder builder,
        string name,
        Action<NetHttpClient> configureClient,
        string tracePath,
        string traceAllPath,
        string discoverPath,
        string traceRoutePath)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configureClient);

        ValidatePath(tracePath, nameof(tracePath));
        ValidatePath(traceAllPath, nameof(traceAllPath));
        ValidatePath(discoverPath, nameof(discoverPath));
        ValidatePath(traceRoutePath, nameof(traceRoutePath));

        string httpClientName = $"PingTrace.Http:{name}";

        IHttpClientBuilder httpClientBuilder = builder.Services.AddHttpClient(httpClientName, configureClient);

        builder.Services.AddScoped<IPingTraceConnector>(provider =>
            new HttpPingTraceConnector(
                name: name,
                clientFactory: provider.GetRequiredService<IHttpClientFactory>(),
                httpClientName: httpClientName,
                tracePath: tracePath,
                traceAllPath: traceAllPath,
                discoverPath: discoverPath,
                traceRoutePath: traceRoutePath,
                logger: provider.GetRequiredService<ILogger<HttpPingTraceConnector>>()));

        return httpClientBuilder;
    }

    private static void ValidatePath(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);

        // A single leading slash is a supported HTTP path rooted at the
        // configured server. On Unix, UriKind.Absolute can instead classify
        // it as a file URI, so exclude that form from the absolute-URI check.
        bool isRootRelativePath = path.StartsWith('/');

        if (path.StartsWith("//", StringComparison.Ordinal) ||
            path.Contains('\\') ||
            (!isRootRelativePath && Uri.TryCreate(path, UriKind.Absolute, out _)))
        {
            throw new ArgumentException(
                "The trace path must be relative to the configured server.",
                parameterName);
        }
    }
}
