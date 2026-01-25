using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Signage.Player.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configure services
builder.Services.AddHttpClient<HeartbeatService>(client =>
{
    var serverUrl = builder.Configuration["ServerUrl"] ?? "http://localhost:8080";
    client.BaseAddress = new Uri(serverUrl);
});

builder.Services.AddHttpClient<UpdateService>(client =>
{
    var serverUrl = builder.Configuration["ServerUrl"] ?? "http://localhost:8080";
    client.BaseAddress = new Uri(serverUrl);
});

builder.Services.AddSingleton<ScreenCaptureService>();
builder.Services.AddHostedService<Signage.Player.PlayerWorker>();

var host = builder.Build();
await host.RunAsync();

