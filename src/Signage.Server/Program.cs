using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Signage.Server.Api;
using Signage.Server.Components;
using Signage.Server.Data;
using Signage.Server.Services;

// CLI utility: print a bcrypt hash for use in initial setup
if (args.Contains("--hash-password"))
{
    Console.Write("Password: ");
    string? password = Console.ReadLine();
    if (!string.IsNullOrWhiteSpace(password))
        Console.WriteLine(BCrypt.Net.BCrypt.HashPassword(password));
    return;
}

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

// Add cookie authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/auth/logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

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

// Migrate database and seed default admin user
using (IServiceScope scope = app.Services.CreateScope())
{
    SignageDbContext dbContext = scope.ServiceProvider.GetRequiredService<SignageDbContext>();
    dbContext.Database.Migrate();

    if (!dbContext.AdminUsers.Any())
    {
        dbContext.AdminUsers.Add(new AdminUser
        {
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin"),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        dbContext.SaveChanges();
    }
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

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

// Logout endpoint
app.MapPost("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).DisableAntiforgery();

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
