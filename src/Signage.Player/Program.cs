using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Signage.Player.Services;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Configure services
builder.Services.AddHttpClient<HeartbeatService>(client =>
{
    string serverUrl = builder.Configuration["ServerUrl"] ?? "https://localhost:7149/";
    client.BaseAddress = new Uri(serverUrl);
});

builder.Services.AddHttpClient<UpdateService>(client =>
{
    string serverUrl = builder.Configuration["ServerUrl"] ?? "https://localhost:7149/";
    client.BaseAddress = new Uri(serverUrl);
});

builder.Services.AddSingleton<ScreenCaptureService>();
builder.Services.AddHostedService<Signage.Player.PlayerWorker>();

IHost host = builder.Build();
await host.RunAsync();
