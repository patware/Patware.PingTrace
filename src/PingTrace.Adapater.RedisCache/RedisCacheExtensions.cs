using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using PingTrace.Core;
using PingTrace.Runtime;

using StackExchange.Redis;

namespace PingTrace.Adapater.RedisCache;

/// <summary>
/// Registers Redis cache destinations for PingTrace.
/// </summary>
public static class RedisCacheExtensions
{
    /// <summary>
    /// Registers a named Redis destination using an existing connection.
    /// </summary>
    /// <param name="builder">The PingTrace registration builder.</param>
    /// <param name="name">The destination name used when tracing.</param>
    /// <returns>The builder for further destination registrations.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="name"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty or consists only of whitespace.
    /// </exception>
    /// <remarks>
    /// Register IConnectionMultiplexer separately.
    /// Connection resolution is deferred until tracing begins, matching
    /// the existing Entity Framework adapter's registration pattern.
    /// The connector borrows the registered connection and does not dispose it.
    /// All destinations registered through this overload resolve the same
    /// unkeyed IConnectionMultiplexer registration.
    /// </remarks>
    public static PingTraceBuilder AddRedisCacheDestination(this PingTraceBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        builder.Services.AddScoped<IPingTraceConnector>(provider =>
            new RedisTraceConnector(
                name: name,
                getConnection: () => provider.GetRequiredService<IConnectionMultiplexer>(),
                logger: provider.GetRequiredService<ILogger<RedisTraceConnector>>()));

        return builder;
    }
}