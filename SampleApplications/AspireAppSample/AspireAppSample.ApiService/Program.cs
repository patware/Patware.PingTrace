using AspireAppSample.ApiService;
using AspireAppSample.ApiService.Services.Simulator;

using Microsoft.EntityFrameworkCore;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;

using PingTrace.Adapter.ActiveDirectory;
using PingTrace.Adapter.EntityFramework;
using PingTrace.Adapter.MicrosoftGraph;
using PingTrace.AspNetCore;
using PingTrace.Runtime;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

/* This is to simulate and validate the PingTrace.Adapter.EntityFramework */

var connectionString = builder.Configuration.GetConnectionString("ApiService");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Connection string 'ApiService' is missing or empty.");
}

builder.Services.AddDbContext<ApiServiceDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});


/* This is to simulate and validate the PingTrace.Adapter.MicrosoftGraph */

builder.Services.AddTransient<SimulatedGraphHttpMessageHandler>();

builder.Services
    .AddHttpClient("SimulatedGraph")
    .ConfigurePrimaryHttpMessageHandler<SimulatedGraphHttpMessageHandler>();

builder.Services.AddScoped<GraphServiceClient>(provider =>
{
    var factory = provider.GetRequiredService<IHttpClientFactory>();

    return new GraphServiceClient(
        factory.CreateClient("SimulatedGraph"),
        new AnonymousAuthenticationProvider(),
        "https://graph.simulated/v1.0");
});

/* This is to simulate and validate the PingTrace.Adapter.ActiveDirectory */

builder.Services.AddScoped<SimulatedActiveDirectoryDiagnosticsReader>();




builder.Services.AddPingTrace(pingTrace =>
{
    /* This is to simulate and validate the PingTrace.Adapter.EntityFramework */
    pingTrace.AddDbContextDestination<ApiServiceDbContext>("ApiServiceDb");

    /* This is to simulate and validate the PingTrace.Adapter.MicrosoftGraph */
    pingTrace.AddGraphOrganizationDestination(
        name: "MicrosoftGraph",
        configuredIdentity: "SIMULATED; tenant:sample-tenant; app:sample-app");

    /* This is to simulate and validate the PingTrace.Adapter.ActiveDirectory */
    pingTrace.AddActiveDirectoryDestination<
        SimulatedActiveDirectoryDiagnosticsReader>(
            "ActiveDirectory",
            @"SIMULATED; CONTOSO\sample-app");
});



var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

string[] summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

app.MapGet("/", () => "API service is running. Navigate to /weatherforecast to see sample data.");

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.MapDefaultEndpoints();

app.MapPingTrace();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
