namespace PingTrace.Core;

/// <summary>
/// Classifies the failure reported by a diagnostic operation.
/// </summary>
/// <remarks>
/// Classification supports filtering and automation.
/// The accompanying message explains the observed failure.
/// Connectors must not infer a more specific cause than the available
/// evidence supports.
/// </remarks>
public enum TraceFailureKind
{
    /// <summary>
    /// No failure was reported.
    /// </summary>
    None = 0,

    /// <summary>
    /// A failure occurred without a more specific classification.
    /// </summary>
    Unknown = 1,

    /// <summary>
    /// The diagnostic operation exceeded its allowed waiting time.
    /// </summary>
    Timeout = 2,

    /// <summary>
    /// Communication with the destination failed.
    /// </summary>
    /// <remarks>
    /// This does not by itself establish that the destination is offline.
    /// </remarks>
    Connection = 3,

    /// <summary>
    /// The destination rejected or could not establish authentication.
    /// </summary>
    Authentication = 4,

    /// <summary>
    /// The requested operation was denied by an access policy.
    /// </summary>
    Authorization = 5,

    /// <summary>
    /// Required diagnostic configuration is missing or invalid.
    /// </summary>
    Configuration = 6,

    /// <summary>
    /// The destination reported an unsuccessful operation.
    /// </summary>
    RemoteError = 7,

    /// <summary>
    /// The response could not be interpreted as the expected diagnostic data.
    /// </summary>
    InvalidResponse = 8
}