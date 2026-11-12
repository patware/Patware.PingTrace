using System;

namespace PingTrace.Core;

/// <summary>
/// Identifies an explicit diagnostic route and its traversal limits.
/// </summary>
/// <remarks>
/// Segments are registered connector aliases, not application names or URLs.
/// Each forwarding application removes the segment it consumes.
/// An empty route requests the receiving application's local trace.
/// </remarks>
public sealed class TraceRouteRequest
{
    /// <summary>
    /// Gets or sets the remaining connector aliases.
    /// </summary>
    public string[] Segments { get; set; } = [];

    /// <summary>
    /// Gets or sets the maximum remaining connector hops.
    /// </summary>
    public int RemainingDepth { get; set; } = 8;

    /// <summary>
    /// Gets or sets the overall UTC deadline.
    /// </summary>
    public DateTimeOffset DeadlineUtc { get; set; } = DateTimeOffset.UtcNow.AddSeconds(30);

    /// <summary>
    /// Gets or sets the application instances already visited on this path.
    /// </summary>
    public Guid[] Ancestors { get; set; } = [];

    /// <summary>
    /// Validates the route and traversal context.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The route or traversal context is invalid.
    /// </exception>
    /// <remarks>
    /// Discovery and routed tracing use the same traversal bounds.
    /// An expired deadline is handled through cancellation.
    /// </remarks>
    public void Validate()
    {
        new DiscoveryRequest
        {
            RemainingDepth = RemainingDepth,
            DeadlineUtc = DeadlineUtc,
            Ancestors = Ancestors
        }.Validate();

        if (Segments is null || Segments.Length > RemainingDepth || Array.Exists(Segments, string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("The route contains invalid segments or exceeds the remaining depth.", nameof(Segments));
        }
    }
}