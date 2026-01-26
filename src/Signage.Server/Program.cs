using Microsoft.EntityFrameworkCore;
using Signage.Server.Components;
using Signage.Server.Data;
using Signage.Server.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire service discovery.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add API controllers
builder.Services.AddControllers();

// Add database context
builder.Services.AddDbContext<SignageDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("signagedb")));

// Add custom services
builder.Services.AddSingleton<WebsiteRecorder>();
builder.Services.AddSingleton<VersionService>();

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

// Map API controllers
app.MapControllers();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
