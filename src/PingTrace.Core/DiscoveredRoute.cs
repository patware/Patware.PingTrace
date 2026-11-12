namespace PingTrace.Core;

/// <summary>
/// Describes a registered destination and the connector aliases used to reach it.
/// </summary>
/// <remarks>
/// Segments are relative to the application producing the discovery result.
/// They are connector registration names, not URLs or server identities.
/// The final segment is the destination alias.
/// </remarks>
public sealed class DiscoveredRoute
{
    /// <summary>
    /// Gets or sets the destination's registered alias.
    /// </summary>
    public string Destination { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ordered connector aliases forming the route.
    /// </summary>
    public string[] Segments { get; set; } = [];
}