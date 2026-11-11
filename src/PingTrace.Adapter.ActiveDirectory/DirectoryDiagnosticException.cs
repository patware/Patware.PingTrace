using PingTrace.Core;

namespace PingTrace.Adapter.ActiveDirectory;

/// <summary>
/// Carries an explicitly identified failure from the built-in directory reader.
/// </summary>
/// <remarks>
/// This separates known diagnostic conditions from arbitrary implementation
/// exceptions. Messages supplied by the reader must be safe to return.
/// </remarks>
internal sealed class DirectoryDiagnosticException : Exception
{
    internal DirectoryDiagnosticException(TraceFailureKind failureKind, string errorCode, string message) : base(message)
    {
        FailureKind = failureKind;
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Gets the known failure classification.
    /// </summary>
    internal TraceFailureKind FailureKind { get; }

    /// <summary>
    /// Gets the stable diagnostic error code.
    /// </summary>
    internal string ErrorCode { get; }
}