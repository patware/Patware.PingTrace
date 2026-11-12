using System.Text.Json;
using FluentAssertions;
using PingTrace.Core;

namespace PingTrace.Runtime.Tests;

/// <summary>
/// Verifies diagnostic outcomes, retained evidence, and wire compatibility.
/// </summary>
[TestClass]
public sealed class TraceResultTests
{
    /// <summary>
    /// Verifies that a failed operation retains previously collected evidence.
    /// </summary>
    [TestMethod]
    public void Fail_PreservesEvidenceAndRecordsCompletion()
    {
        // Arrange
        TraceResult result = new()
        {
            Name = "Cache", Identity = "user", MachineName = "dev-cache", Payload = "partial"
        };
        DateTime started = result.StartedAt;

        // Act
        result.Fail(TraceFailureKind.Timeout, "Cache.Timeout", "The operation timed out.");

        // Assert
        result.Status.Should().Be(TraceStatus.Failed);
        result.FailureKind.Should().Be(TraceFailureKind.Timeout);
        result.ErrorCode.Should().Be("Cache.Timeout");
        result.Message.Should().Be("The operation timed out.");
        result.Payload.Should().Be("partial");
        result.Name.Should().Be("Cache");
        result.Identity.Should().Be("user");
        result.MachineName.Should().Be("dev-cache");
        result.StartedAt.Should().Be(started);
        result.FinishedAt.Should().NotBe(default);
    }

    /// <summary>
    /// Verifies that a subsequent success does not retain stale error fields.
    /// </summary>
    [TestMethod]
    public void Finish_ClearsPreviousFailure()
    {
        // Arrange
        TraceResult result = new();
        result.Fail(TraceFailureKind.Connection, "Cache.Connection", "Connection failed.");

        // Act
        result.Finish("PONG");

        // Assert
        result.Status.Should().Be(TraceStatus.Succeeded);
        result.FailureKind.Should().Be(TraceFailureKind.None);
        result.ErrorCode.Should().BeEmpty();
        result.Message.Should().BeEmpty();
        result.Payload.Should().Be("PONG");
    }

    /// <summary>
    /// Verifies that older JSON without an outcome does not imply success.
    /// </summary>
    [TestMethod]
    public void LegacyJson_HasUnknownOutcome()
    {
        // Arrange
        const string json = """{"name":"Legacy","payload":"old diagnostics"}""";
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);

        // Act
        TraceResult result = JsonSerializer.Deserialize<TraceResult>(json, options)!;

        // Assert
        result.Status.Should().Be(TraceStatus.Unknown);
        result.Name.Should().Be("Legacy");
        result.Payload.Should().Be("old diagnostics");
    }

    /// <summary>
    /// Verifies that HTTP-style serialization preserves diagnostic fields.
    /// </summary>
    [TestMethod]
    public void FailedResult_RoundTripsThroughJson()
    {
        // Arrange
        TraceResult original = new() { Name = "Cache", Payload = "partial" };
        original.Fail(TraceFailureKind.Authentication, "Cache.Auth", "Authentication failed.");
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);

        // Act
        TraceResult restored = JsonSerializer.Deserialize<TraceResult>(
            JsonSerializer.Serialize(original, options), options)!;

        // Assert
        restored.Should().BeEquivalentTo(original);
    }

    /// <summary>
    /// Verifies that invalid failure classifications cannot mutate the result.
    /// </summary>
    /// <param name="kind">The invalid classification.</param>
    [TestMethod]
    [DataRow(TraceFailureKind.None)]
    [DataRow((TraceFailureKind)999)]
    public void Fail_InvalidKind_DoesNotChangeOutcome(TraceFailureKind kind)
    {
        // Arrange
        TraceResult result = new();
        result.Finish("existing");

        // Act
        Action act = () => result.Fail(kind, "Code", "Message");

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
        result.Status.Should().Be(TraceStatus.Succeeded);
        result.Payload.Should().Be("existing");
    }
}
