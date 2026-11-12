using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using PingTrace.Core;

namespace PingTrace.Adapter.EntityFramework;

/// <summary>
/// Queries SQL Server diagnostic values through a registered DbContext.
/// </summary>
/// <typeparam name="TContext">
/// The registered context configured to use SQL Server.
/// </typeparam>
/// <remarks>
/// The diagnostic query returns the server instance name, current login
/// name, and database name without reading application tables or changing
/// application data. Its SQL Server-specific functions limit provider
/// support despite the generic DbContext constraint.
///
/// The dependency-injection scope owns the context. Resolution is deferred
/// until tracing begins so local diagnostics do not construct the context.
/// The connector must not run concurrently with other operations using
/// the same context.
///
/// Timing is measured by the calling application and includes context
/// resolution and query execution.
///
/// Handled exceptions are passed to the application's logging pipeline
/// because the public diagnostic result deliberately contains only
/// selected failure details. The application controls log retention.
/// </remarks>
internal sealed class DbContextPingTraceConnector<TContext> : IPingTraceConnector where TContext : DbContext
{
    private readonly Func<TContext> _resolveContext;
    private readonly ILogger<DbContextPingTraceConnector<TContext>> _logger;

    internal DbContextPingTraceConnector(string name, Func<TContext> resolveContext, ILogger<DbContextPingTraceConnector<TContext>> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(resolveContext);
        ArgumentNullException.ThrowIfNull(logger);

        Name = name;
        _resolveContext = resolveContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Known database exceptions become failed diagnostic results.
    /// Generic database exceptions do not establish whether the cause was
    /// authentication, connectivity, or another provider-specific condition.
    ///
    /// The query requests at most two rows so an unexpected result count can
    /// be reported explicitly without catching unrelated InvalidOperationExceptions.
    ///
    /// Context resolution remains deferred. The application owns the context.
    /// Cancellation and unexpected implementation errors propagate.
    /// </remarks>
    public async Task<IReadOnlyList<TraceResult>> TraceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        TraceResult result = new()
        {
            Name = Name
        };

        try
        {
            TContext context = _resolveContext();

            cancellationToken.ThrowIfCancellationRequested();

            List<DatabaseDiagnostics> rows = await context.Database
                .SqlQuery<DatabaseDiagnostics>($"""
                    SELECT
                        @@SERVERNAME AS [MachineName],
                        SUSER_NAME() AS [Identity],
                        DB_NAME() AS [Payload]
                    """)
                .Take(2)
                .ToListAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (rows.Count != 1)
            {
                result.Fail(
                    TraceFailureKind.InvalidResponse,
                    "Database.UnexpectedRowCount",
                    $"The diagnostic query returned {rows.Count} rows; exactly one was expected.");

                _logger.LogWarning(
                    "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                    Name,
                    result.ErrorCode,
                    result.Message);

                return [result];
            }

            DatabaseDiagnostics diagnostics = rows[0];

            result.MachineName = diagnostics.MachineName ?? string.Empty;
            result.Identity = diagnostics.Identity ?? string.Empty;
            result.Finish(diagnostics.Payload ?? string.Empty);
        }
        catch (TimeoutException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();

            result.Fail(
                TraceFailureKind.Timeout,
                "Database.DiagnosticTimeout",
                "The database diagnostic operation timed out.");

            _logger.LogWarning(
                exception,
                "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                Name,
                result.ErrorCode,
                result.Message);
        }
        catch (DbException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();

            result.Fail(
                TraceFailureKind.Unknown,
                "Database.ProviderFailure",
                $"The database provider reported a failure ({exception.GetType().Name}, error code {exception.ErrorCode}).");

            _logger.LogWarning(
                exception,
                "Trace destination {Destination} failed with {ErrorCode}: {Message}",
                Name,
                result.ErrorCode,
                result.Message);
        }

        return [result];
    }
}