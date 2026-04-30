using Microsoft.EntityFrameworkCore;
using Signage.Server.Api;
using Signage.Server.Components;
using Signage.Server.Data;
using Signage.Server.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire service discovery.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(o => o.MaximumReceiveMessageSize = 512L * 1024 * 1024); // 512 MB for video uploads

// Add database context
builder.Services.AddDbContext<SignageDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("signagedb")));

// Add custom services
builder.Services.AddSingleton<WebsiteRecorder>();
builder.Services.AddSingleton<RenderQueue>();
builder.Services.AddHostedService<RenderWorkerService>();
builder.Services.AddScoped<ContentSyncService>();
builder.Services.AddHostedService<ContentSyncWorkerService>();
builder.Services.AddScoped<VersionService>();

// Add HttpClient for calling our own API
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();

WebApplication app = builder.Build();

// Migrate database at startup
using (IServiceScope scope = app.Services.CreateScope())
{
    SignageDbContext dbContext = scope.ServiceProvider.GetRequiredService<SignageDbContext>();
    dbContext.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

// Map default endpoints
app.MapDefaultEndpoints();

// Register Minimal APIs
app.MapContentApi();
app.MapDeviceApi();
app.MapPlaylistApi();
app.MapUpdateApi();
app.MapVersionApi();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
