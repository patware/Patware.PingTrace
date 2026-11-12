using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PingTrace.Tests;

/// <summary>
/// Captures connector warnings without depending on a logging backend.
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    internal ConcurrentQueue<(Exception? Exception, string Message)> Warnings { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose() { }

    private sealed class CapturingLogger(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel == LogLevel.Warning && category.StartsWith("PingTrace.", StringComparison.Ordinal);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                owner.Warnings.Enqueue((exception, formatter(state, exception)));
            }
        }
    }
}
