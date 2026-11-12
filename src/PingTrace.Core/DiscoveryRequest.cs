using System;

namespace PingTrace.Core;

/// <summary>
/// Carries traversal limits for discovering registered destinations.
/// </summary>
/// <remarks>
/// Each remote call receives a new request with a reduced depth and an
/// extended ancestor path. The original deadline is preserved so remote
/// participants do not restart the operation's time budget.
/// </remarks>
public sealed class DiscoveryRequest
{
    /// <summary>
    /// Gets or sets the maximum remaining connector-path length.
    /// </summary>
    /// <remarks>
    /// Zero permits inspection of the local application only.
    /// Existing destinations beyond that limit make discovery incomplete.
    /// </remarks>
    public int RemainingDepth { get; set; } = 8;

    /// <summary>
    /// Gets or sets the overall UTC discovery deadline.
    /// </summary>
    public DateTimeOffset DeadlineUtc { get; set; } = DateTimeOffset.UtcNow.AddSeconds(30);

    /// <summary>
    /// Gets or sets the application-instance identifiers already visited
    /// on this path.
    /// </summary>
    /// <remarks>
    /// Ancestors are path-local so shared dependencies can be discovered
    /// through different branches without being mistaken for cycles.
    /// </remarks>
    public Guid[] Ancestors { get; set; } = [];

    /// <summary>
    /// Validates the supplied traversal limits.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The depth, ancestor path, or deadline is invalid.
    /// </exception>
    /// <remarks>
    /// An expired deadline is valid input and is subsequently handled
    /// through cancellation.
    /// </remarks>
    public void Validate()
    {
        if (RemainingDepth < 0 || RemainingDepth > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(RemainingDepth));
        }

        if (Ancestors is null ||
            Ancestors.Length > 16 ||
            Ancestors.Length + RemainingDepth > 16 ||
            Array.Exists(Ancestors, ancestor => ancestor == Guid.Empty))
        {
            throw new ArgumentException("The discovery ancestor path is invalid.", nameof(Ancestors));
        }

        if (DeadlineUtc == default)
        {
            throw new ArgumentException("A discovery deadline is required.", nameof(DeadlineUtc));
        }
    }
}