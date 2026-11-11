namespace PingTrace.Core;

/// <summary>
/// Describes the outcome reported by a diagnostic operation.
/// </summary>
/// <remarks>
/// The status describes this result's operation, not the combined health
/// of every dependency returned by a recursive trace.
/// </remarks>
public enum TraceStatus
{
    /// <summary>
    /// No explicit outcome was reported.
    /// </summary>
    /// <remarks>
    /// This is also the default for responses from older producers
    /// that do not supply a status.
    /// </remarks>
    Unknown = 0,

    /// <summary>
    /// The diagnostic operation completed successfully.
    /// </summary>
    Succeeded = 1,

    /// <summary>
    /// The diagnostic operation reported a failure.
    /// </summary>
    Failed = 2
}