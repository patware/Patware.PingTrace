using AspireAppSample.Web;
using AspireAppSample.Web.Components;

using PingTrace.Adapter.RedisCache;
using PingTrace.Adapter.HttpClient;
using PingTrace.AspNetCore;
using PingTrace.Runtime;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();
builder.AddRedisOutputCache("cache");

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

Uri apiServiceUri = new("https+http://apiservice");

builder.Services.AddHttpClient<WeatherApiClient>(client =>
    {
        // This URL uses "https+http://" to indicate HTTPS is preferred over HTTP.
        // Learn more about service discovery scheme resolution at https://aka.ms/dotnet/sdschemes.
        client.BaseAddress = apiServiceUri;
    });

builder.Services.AddPingTrace(pingTrace =>
{
#pragma warning disable EXTEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    pingTrace.AddHttpDestination(
        name: "WeatherApiClient",
        configureClient: client =>
            {
                client.BaseAddress = apiServiceUri;
                client.Timeout = TimeSpan.FromSeconds(5);
            })
    .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

    pingTrace.AddRedisCacheDestination("RedisCache");
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.UseOutputCache();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.MapPingTrace();

app.Run();
