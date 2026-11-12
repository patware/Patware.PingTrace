using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using PingTrace.Core;

namespace PingTrace.AspNetCore;

/// <summary>
/// Provides endpoint registration for PingTrace diagnostics.
/// </summary>
public static class PingTraceEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps Ping, Trace, TraceAll, and Discover endpoints under a common prefix.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="prefix">The common route prefix.</param>
    /// <returns>The group for configuring endpoint conventions.</returns>
    /// <remarks>
    /// The group contains GET endpoints for Ping, Trace, and TraceAll,
    /// plus a POST endpoint for continuing recursive traversal.
    /// Applying authorization to the group protects all mapped operations.
    /// The host owns authentication and exception handling.
    /// Discovery reports registered destinations without executing leaf probes.
    /// Incomplete discovery returns a report containing branch issues.
    /// Applying authorization to the group also protects discovery endpoints.
    /// </remarks>
    public static RouteGroupBuilder MapPingTrace(this IEndpointRouteBuilder endpoints, string prefix = "/pingtrace")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        RouteGroupBuilder group = endpoints.MapGroup(prefix);

        group.MapGet("/Ping", HandlePing);
        group.MapGet("/Trace", HandleTraceAsync);
        group.MapGet("/TraceAll", HandleTraceAllAsync);
        group.MapPost("/TraceAll", HandleRecursiveTraceAllAsync);
        group.MapGet("/Discover", HandleDiscoverAsync);
        group.MapPost("/Discover", HandleRecursiveDiscoverAsync);
        group.MapPost("/TraceRoute", HandleTraceRouteAsync);

        return group;

    }

    private static IResult HandlePing([FromServices] IPingTrace pingTrace)
    {
        return Results.Text(pingTrace.Ping(), "text/plain");
    }


    private static async Task<IResult> HandleTraceAsync(HttpContext context, [FromServices] IPingTrace pingTrace)
    {
        CancellationToken cancellationToken = context.RequestAborted;

        if (!context.Request.Query.TryGetValue("destination", out var values))
        {
            TraceResult result = await pingTrace.TraceAsync(cancellationToken);

            return Results.Ok(result);
        }

        string destination = values.ToString();

        if (string.IsNullOrWhiteSpace(destination))
        {
            return Results.BadRequest("Destination must not be empty.");
        }

        try
        {
            IReadOnlyList<TraceResult> results = await pingTrace.TraceAsync(destination, cancellationToken);

            return Results.Ok(results);
        }
        catch (TraceRoutingException exception)
        {
            return RoutingErrorResult(exception.Error);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound("The requested destination is not known.");
        }
    }

    private static async Task<IResult> HandleTraceAllAsync(HttpContext context, [FromServices] IPingTrace pingTrace)
    {
        IReadOnlyList<TraceResult> results = await pingTrace.TraceAllAsync(cancellationToken: context.RequestAborted);

        return Results.Ok(results);
    }

    private static async Task<IResult> HandleRecursiveTraceAllAsync(
        HttpContext context,
        [FromServices] IPingTrace pingTrace,
        [FromBody] TraceAllRequest request)
    {
        if (request is null)
        {
            return Results.BadRequest("A traversal request is required.");
        }

        try
        {
            request.Validate();
        }
        catch (ArgumentException)
        {
            return Results.BadRequest("The traversal request is invalid.");
        }

        IReadOnlyList<TraceResult> results = await pingTrace.TraceAllAsync(request, context.RequestAborted);

        return Results.Ok(results);
    }

    private static async Task<IResult> HandleDiscoverAsync(
        HttpContext context,
        [FromServices] IPingTrace pingTrace)
    {
        if (pingTrace is not IPingTraceDiscovery discovery)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status501NotImplemented,
                title: "This PingTrace implementation does not support discovery.");
        }

        DiscoveryResult result = await discovery.DiscoverAsync(cancellationToken: context.RequestAborted);

        return Results.Ok(result);
    }

    private static async Task<IResult> HandleRecursiveDiscoverAsync(
        HttpContext context,
        [FromServices] IPingTrace pingTrace,
        [FromBody] DiscoveryRequest request)
    {
        if (request is null)
        {
            return Results.BadRequest("A discovery request is required.");
        }

        try
        {
            request.Validate();
        }
        catch (ArgumentException)
        {
            return Results.BadRequest("The discovery request is invalid.");
        }

        if (pingTrace is not IPingTraceDiscovery discovery)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status501NotImplemented,
                title: "This PingTrace implementation does not support discovery.");
        }

        DiscoveryResult result = await discovery.DiscoverAsync(request, context.RequestAborted);

        return Results.Ok(result);
    }

    private static async Task<IResult> HandleTraceRouteAsync(
        HttpContext context,
        [FromServices] IPingTrace pingTrace,
        [FromBody] TraceRouteRequest request)
    {
        if (request is null)
        {
            return Results.BadRequest("A route request is required.");
        }

        try
        {
            request.Validate();
        }
        catch (ArgumentException)
        {
            return Results.BadRequest("The route request is invalid.");
        }

        if (pingTrace is not IPingTraceRouter router)
        {
            return RoutingErrorResult(new TraceRoutingError
            {
                Protocol = TraceRoutingError.ProtocolName,
                ErrorCode = "Routing.ForwardingNotSupported",
                Message = "This PingTrace implementation does not support routed tracing."
            });
        }

        try
        {
            IReadOnlyList<TraceResult> results = await router.TraceRouteAsync(request, context.RequestAborted);

            return Results.Ok(results);
        }
        catch (TraceRoutingException exception)
        {
            return RoutingErrorResult(exception.Error);
        }
    }

    private static IResult RoutingErrorResult(TraceRoutingError error)
    {
        int statusCode = error.ErrorCode switch
        {
            "Routing.DestinationNotFound" or
            "Routing.SegmentNotRegistered" =>
                StatusCodes.Status404NotFound,

            "Routing.AmbiguousDestination" or
            "Routing.Cycle" =>
                StatusCodes.Status409Conflict,

            "Routing.DiscoveryIncomplete" => StatusCodes.Status503ServiceUnavailable,

            "Routing.ForwardingNotSupported" => StatusCodes.Status501NotImplemented,

            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Json(error, statusCode: statusCode);
    }
}
