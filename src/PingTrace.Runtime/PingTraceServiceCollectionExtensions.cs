using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using PingTrace.Core;

namespace PingTrace.Runtime;

/// <summary>
/// Provides registration methods for the PingTrace runtime.
/// </summary>
public static class PingTraceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the default PingTrace runtime and optional dependencies.
    /// </summary>
    /// <param name="services">
    /// The service collection receiving the registrations.
    /// </param>
    /// <param name="configure">
    /// An optional callback that registers dependency adapters.
    /// </param>
    /// <returns>The service collection for further registration.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The callback executes during registration, before the service
    /// provider is built. An existing IPingTrace registration is preserved.
    /// Omitting the callback registers a default leaf implementation.
    ///
    /// Logging services are registered so connectors can report original
    /// exceptions when returning failed diagnostics. The application controls
    /// logging providers, filtering, and retention.
    /// </remarks>
    public static IServiceCollection AddPingTrace(this IServiceCollection services, Action<PingTraceBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLogging();

        services.TryAddSingleton<TraceAllNode>();
        services.TryAddScoped<IPingTrace, DefaultPingTrace>();

        configure?.Invoke(new PingTraceBuilder(services));

        return services;
    }
}