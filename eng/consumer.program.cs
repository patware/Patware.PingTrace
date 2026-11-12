using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PingTrace.AspNetCore;
using PingTrace.Core;
using PingTrace.Runtime;

internal static class Program
{
    public static async Task Main()
    {
        string[] assemblies = __ASSEMBLIES__;

        foreach (string name in assemblies)
        {
            Assembly.Load(new AssemblyName(name));
        }

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddPingTrace();

        await using var app = builder.Build();
        app.MapPingTrace();

        // Materialize the endpoint definitions without starting a server.
        var routes = (IEndpointRouteBuilder)app;
        int endpointCount = routes.DataSources.Sum(
            source => source.Endpoints.Count);

        if (endpointCount != 7)
        {
            throw new InvalidOperationException(
                $"Expected seven PingTrace endpoints; found {endpointCount}.");
        }

        using var scope = app.Services.CreateScope();
        var pingTrace = scope.ServiceProvider
            .GetRequiredService<IPingTrace>();

        if (pingTrace.Ping() != "Pong")
        {
            throw new InvalidOperationException(
                "Ping did not return Pong.");
        }

        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(10));

        TraceResult local = await pingTrace.TraceAsync(timeout.Token);

        if (string.IsNullOrWhiteSpace(local.Name))
        {
            throw new InvalidOperationException(
                "Local Trace returned no application name.");
        }

        IReadOnlyList<TraceResult> results =
            await pingTrace.TraceAllAsync(
                cancellationToken: timeout.Token);

        if (results.Count != 1 || results[0].Name != local.Name)
        {
            throw new InvalidOperationException(
                "A dependency-free TraceAll must return the local application.");
        }

        Console.WriteLine("PingTrace package smoke test passed.");
    }
}