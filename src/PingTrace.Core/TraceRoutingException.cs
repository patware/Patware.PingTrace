using System;

namespace PingTrace.Core;

/// <summary>
/// Reports a known diagnostic-routing failure.
/// </summary>
/// <remarks>
/// Hosting integrations translate this exception into a structured routing
/// response. Unexpected implementation exceptions continue to propagate.
/// </remarks>
public sealed class TraceRoutingException : Exception
{
    /// <summary>
    /// Initializes an exception containing a routing failure.
    /// </summary>
    /// <param name="error">The structured routing failure.</param>
    /// <exception cref="ArgumentNullException">
    /// The error is null.
    /// </exception>
    public TraceRoutingException(TraceRoutingError error) : base(error?.Message)
    {
        ArgumentNullException.ThrowIfNull(error);
        Error = error;
    }

    /// <summary>
    /// Gets the structured routing failure.
    /// </summary>
    public TraceRoutingError Error { get; }
}