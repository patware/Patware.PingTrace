using System;
using System.Collections.Generic;

namespace PingTrace.Core;

/// <summary>
/// Contains known destination routes and any incomplete discovery branches.
/// </summary>
/// <remarks>
/// Discovery describes registrations without establishing connectivity
/// to leaf dependencies. An incomplete result must not be used as proof
/// that an undiscovered destination does not exist.
/// </remarks>
public sealed class DiscoveryResult
{
    /// <summary>
    /// Gets the maximum number of routes in one discovery response.
    /// </summary>
    public const int MaximumRoutes = 256;

    /// <summary>
    /// Gets the maximum number of issues in one discovery response.
    /// </summary>
    public const int MaximumIssues = 64;

    /// <summary>
    /// Gets or sets the responding application's name.
    /// </summary>
    /// <remarks>
    /// This can be empty when communication failed before the application
    /// identified itself. It is descriptive metadata, not a routing segment.
    /// </remarks>
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether every relevant branch was discovered.
    /// </summary>
    /// <remarks>
    /// Defaults to false so an omitted value cannot imply complete discovery.
    /// Complete results have no issues; incomplete results explain at least
    /// one limitation.
    /// </remarks>
    public bool IsComplete { get; set; }

    /// <summary>
    /// Gets or sets routes relative to the responding application.
    /// </summary>
    public List<DiscoveredRoute> Routes { get; set; } = [];

    /// <summary>
    /// Gets or sets the limitations encountered during discovery.
    /// </summary>
    public List<DiscoveryIssue> Issues { get; set; } = [];

    /// <summary>
    /// Validates the response structure against the requested depth.
    /// </summary>
    /// <param name="maximumDepth">The maximum permitted route length.</param>
    /// <exception cref="ArgumentException">
    /// The response contains invalid, inconsistent, or excessive data.
    /// </exception>
    public void Validate(int maximumDepth)
    {
        if (maximumDepth < 0 || maximumDepth > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDepth));
        }

        if (Routes is null || Issues is null || Routes.Count > MaximumRoutes || Issues.Count > MaximumIssues)
        {
            throw new ArgumentException("The discovery collections are missing or exceed their limits.");
        }

        if (IsComplete != (Issues.Count == 0))
        {
            throw new ArgumentException("Discovery completeness does not agree with its issues.");
        }

        if (IsComplete && string.IsNullOrWhiteSpace(ApplicationName))
        {
            throw new ArgumentException("Complete discovery must identify the responding application.");
        }

        foreach (DiscoveredRoute route in Routes)
        {
            if (route is null ||
                string.IsNullOrWhiteSpace(route.Destination) ||
                !IsValidPath(route.Segments, maximumDepth, allowEmpty: false) ||
                !string.Equals(
                    route.Destination,
                    route.Segments[^1],
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The discovery response contains an invalid route.");
            }
        }

        foreach (DiscoveryIssue issue in Issues)
        {
            if (issue is null ||
                !IsValidPath(issue.Segments, maximumDepth, allowEmpty: true) ||
                string.IsNullOrWhiteSpace(issue.ErrorCode) ||
                string.IsNullOrWhiteSpace(issue.Message))
            {
                throw new ArgumentException("The discovery response contains an invalid issue.");
            }
        }
    }

    private static bool IsValidPath(string[] segments, int maximumDepth, bool allowEmpty)
    {
        return segments is not null &&
            (allowEmpty || segments.Length > 0) &&
            segments.Length <= maximumDepth &&
            !Array.Exists(segments, string.IsNullOrWhiteSpace);
    }
}