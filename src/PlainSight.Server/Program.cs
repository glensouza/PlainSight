using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Api;
using PlainSight.Server.Components;
using PlainSight.Server.Data;
using PlainSight.Server.Services;

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
builder.Services.AddDbContext<PlainSightDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("plainsightdb")));

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
    PlainSightDbContext dbContext = scope.ServiceProvider.GetRequiredService<PlainSightDbContext>();
    dbContext.Database.Migrate();

    if (!dbContext.AdminUsers.Any())
    {
        string initialPassword = GenerateInitialPassword();
        dbContext.AdminUsers.Add(new AdminUser
        {
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(initialPassword),
            IsActive = true,
            Role = AdminUserRole.Admin,
            MustChangePassword = true,
            CreatedAt = DateTime.UtcNow
        });
        dbContext.SaveChanges();

        ILogger startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Startup");
        startupLogger.LogWarning(
            "Created initial admin account. Username: admin  Password: {Password}  " +
            "You will be required to change this password on first login.",
            initialPassword);
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

// Redirect authenticated users who must change their password.
// Only intercept page navigations — static assets (CSS, JS, images) must pass through
// so that the /change-password page itself can load its stylesheets and scripts.
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true
        && ctx.User.HasClaim("must_change_password", "true")
        && !ctx.Request.Path.StartsWithSegments("/change-password")
        && !ctx.Request.Path.StartsWithSegments("/auth")
        && !ctx.Request.Path.StartsWithSegments("/_blazor")
        && !ctx.Request.Path.StartsWithSegments("/_framework")
        && !ctx.Request.Path.StartsWithSegments("/login")
        && !Path.HasExtension(ctx.Request.Path.Value))
    {
        ctx.Response.Redirect("/change-password");
        return;
    }
    await next();
});

app.UseAntiforgery();

// Logout endpoint
app.MapPost("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

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

static string GenerateInitialPassword()
{
    const string chars = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!@#$";
    byte[] randomBytes = new byte[14];
    System.Security.Cryptography.RandomNumberGenerator.Fill(randomBytes);
    char[] result = new char[14];
    for (int i = 0; i < 14; i++)
        result[i] = chars[randomBytes[i] % chars.Length];
    return new string(result);
}
