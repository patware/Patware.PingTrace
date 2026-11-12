using System;

namespace PingTrace.Core;

/// <summary>
/// Describes a diagnostic operation, its outcome, collected evidence,
/// and execution timing.
/// </summary>
/// <remarks>
/// Producers complete an operation with Finish or Fail.
/// A successful result confirms only the operation performed; it does
/// not establish that the destination matches an expected environment.
///
/// Properties remain writable to support the existing result model
/// and HTTP serialization. Consumers should treat remote results as
/// externally supplied data.
///
/// Parent timings may include dependency execution, so durations
/// from multiple results can overlap.
/// </remarks>
public class TraceResult
{
    /// <summary>
    /// Initializes a result with the current local start time.
    /// </summary>
    public TraceResult()
    {
        StartedAt = DateTime.Now;
    }

    /// <summary>
    /// Gets or sets the owner's registered destination name,
    /// or the application name for a local root result.
    /// </summary>
    /// <remarks>
    /// When an application is reached through an HTTP connector, its result
    /// uses the destination name registered by the calling application.
    /// Descendant names are assigned by their respective immediate owners.
    /// Names are therefore scoped to their owner, not globally unique.
    /// </remarks>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the identity associated with the operation.
    /// </summary>
    /// <remarks>
    /// Connectors should document whether this value is configured
    /// locally or reported by the destination.
    /// </remarks>
    public string Identity { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the machine, server, or host associated with the operation.
    /// </summary>
    /// <remarks>
    /// A configured endpoint is not proof of the responding server's
    /// identity. Connectors should document the source of this value.
    /// </remarks>
    public string MachineName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the start timestamp.
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// Gets or sets the completion timestamp.
    /// </summary>
    /// <remarks>
    /// This remains the default value until completion is recorded.
    /// </remarks>
    public DateTime FinishedAt { get; set; }

    /// <summary>
    /// Gets or sets diagnostic evidence supplied by the producer.
    /// </summary>
    /// <remarks>
    /// The payload may contain text or serialized diagnostic data.
    /// Evidence collected before a failure can remain available.
    /// </remarks>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the explicitly reported outcome.
    /// </summary>
    public TraceStatus Status { get; set; } = TraceStatus.Unknown;

    /// <summary>
    /// Gets or sets the classification of a reported failure.
    /// </summary>
    public TraceFailureKind FailureKind { get; set; } = TraceFailureKind.None;

    /// <summary>
    /// Gets or sets the connector-defined failure code.
    /// </summary>
    /// <remarks>
    /// Codes should remain stable and independent of message wording.
    /// For example, Redis.PingTimeout identifies a particular failure.
    /// This value is empty when no failure has been reported.
    /// </remarks>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a human-readable explanation of the failure.
    /// </summary>
    /// <remarks>
    /// Explain the observed failure without claiming an unverified cause.
    /// Do not include credentials, connection strings, or unfiltered
    /// exception messages.
    /// </remarks>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Records successful completion and its diagnostic payload.
    /// </summary>
    /// <param name="payload">The collected diagnostic evidence.</param>
    /// <remarks>
    /// Repeated completion replaces the previous outcome.
    /// Failure details are cleared to keep the result consistent.
    /// Identity, machine name, and start time are preserved.
    /// </remarks>
    public void Finish(string payload)
    {
        Payload = payload;
        Status = TraceStatus.Succeeded;
        FailureKind = TraceFailureKind.None;
        ErrorCode = string.Empty;
        Message = string.Empty;
        FinishedAt = DateTime.Now;
    }

    /// <summary>
    /// Records failed completion while preserving collected evidence.
    /// </summary>
    /// <param name="failureKind">The observed failure classification.</param>
    /// <param name="errorCode">A stable, connector-defined failure code.</param>
    /// <param name="message">A safe explanation of the observed failure.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The classification is undefined or is None.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// The error code or message is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The error code or message is empty or whitespace.
    /// </exception>
    /// <remarks>
    /// Preserving payload and identity metadata makes partial evidence
    /// available even when the diagnostic operation fails.
    /// Cancellation should propagate rather than be recorded as a
    /// destination failure.
    /// </remarks>
    public void Fail(TraceFailureKind failureKind, string errorCode, string message)
    {
        if (!Enum.IsDefined(typeof(TraceFailureKind), failureKind) || failureKind == TraceFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Status = TraceStatus.Failed;
        FailureKind = failureKind;
        ErrorCode = errorCode;
        Message = message;
        FinishedAt = DateTime.Now;
    }

    /// <summary>
    /// Gets the interval between the recorded start and completion times.
    /// </summary>
    /// <remarks>
    /// Read after completion. This preserves the existing wall-clock
    /// timing behavior and does not measure a running duration.
    /// </remarks>
    public TimeSpan Elapsed => FinishedAt - StartedAt;
}