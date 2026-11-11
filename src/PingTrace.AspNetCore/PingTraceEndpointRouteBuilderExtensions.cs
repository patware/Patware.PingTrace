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
    /// Maps Ping, Trace, and TraceAll endpoints under a common prefix.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="prefix">The common route prefix.</param>
    /// <returns>The group for configuring endpoint conventions.</returns>
    /// <remarks>
    /// The group contains GET endpoints for Ping, Trace, and TraceAll,
    /// plus a POST endpoint for continuing recursive traversal.
    /// Applying authorization to the group protects all mapped operations.
    /// The host owns authentication and exception handling.
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

}
