using System.Collections.Generic;

namespace PingTrace.Core;

/// <summary>
/// Describes a failure to resolve or execute a diagnostic route.
/// </summary>
/// <remarks>
/// This describes routing, not the success or failure of a dependency probe.
/// The protocol marker distinguishes routing responses from unrelated HTTP
/// errors such as a proxy-generated 404.
/// </remarks>
public sealed class TraceRoutingError
{
    /// <summary>
    /// Gets the supported routing-error protocol identifier.
    /// </summary>
    public const string ProtocolName = "PingTrace.Routing.v1";

    /// <summary>
    /// Gets or sets the protocol identifier supplied by the producer.
    /// </summary>
    public string Protocol { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the stable routing error code.
    /// </summary>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the explanation of the routing failure.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets known candidate routes when resolution cannot select one.
    /// </summary>
    public List<DiscoveredRoute> Candidates { get; set; } = [];

    /// <summary>
    /// Gets or sets discovery limitations relevant to resolution.
    /// </summary>
    public List<DiscoveryIssue> Issues { get; set; } = [];
}