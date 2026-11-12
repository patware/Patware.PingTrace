namespace PingTrace.Core;

/// <summary>
/// Explains why a branch could not be completely discovered.
/// </summary>
/// <remarks>
/// Issues describe discovery limitations, not the health of a registered
/// dependency. An empty path identifies the application producing the result.
/// </remarks>
public sealed class DiscoveryIssue
{
    /// <summary>
    /// Gets or sets the connector path to the affected branch.
    /// </summary>
    public string[] Segments { get; set; } = [];

    /// <summary>
    /// Gets or sets a stable discovery issue identifier.
    /// </summary>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a safe explanation of the observed limitation.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}