using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using PingTrace.Core;
using PingTrace.Runtime;

namespace PingTrace.Adapter.EntityFramework;

/// <summary>
/// Registers Entity Framework Core destinations for PingTrace.
/// </summary>
public static class EntityFrameworkExtensions
{
    /// <summary>
    /// Registers a named SQL Server diagnostic destination using an existing
    /// DbContext registration.
    /// </summary>
    /// <typeparam name="TContext">
    /// The registered DbContext type configured to use SQL Server.
    /// </typeparam>
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
    /// Register TContext separately with the SQL Server provider and options.
    /// The generic constraint accepts DbContext types, but the diagnostic
    /// query uses SQL Server-specific functions.
    ///
    /// Registration adds a scoped connector and does not access the database.
    /// Context resolution is deferred until this destination is traced.
    /// The dependency-injection scope owns context disposal.
    ///
    /// The returned database result contains the server instance name,
    /// current login name, and database name. Successful execution does not
    /// validate application tables, migrations, or write permissions.
    /// </remarks>
    public static PingTraceBuilder AddDbContextDestination<TContext>(
        this PingTraceBuilder builder,
        string name)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        builder.Services.AddScoped<IPingTraceConnector>(provider =>
            new DbContextPingTraceConnector<TContext>(
                name: name,
                resolveContext: () => provider.GetRequiredService<TContext>(),
                logger: provider.GetRequiredService<ILogger<DbContextPingTraceConnector<TContext>>>()));

        return builder;
    }
}