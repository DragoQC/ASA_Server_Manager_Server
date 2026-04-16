using AsaServerManager.Web.Components;
using AsaServerManager.Web.Constants;
using AsaServerManager.Web.Data;
using AsaServerManager.Web.Hubs;
using AsaServerManager.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

bool bootstrapDatabase = args.Contains("--bootstrap-db", StringComparer.OrdinalIgnoreCase);
string[] filteredArgs = args.Where(arg => !string.Equals(arg, "--bootstrap-db", StringComparison.OrdinalIgnoreCase)).ToArray();

WebApplicationBuilder builder = WebApplication.CreateBuilder(filteredArgs);
builder.WebHost.UseUrls("http://0.0.0.0:8000");

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DetailedErrors = builder.Environment.IsDevelopment() ||
                                 builder.Configuration.GetValue<bool>("DetailedErrors");
    });
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddHttpClient("proton-ge", client =>
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ASA-Server-Manager/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    });
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

string databasePath = Path.Combine(builder.Environment.ContentRootPath, "Data", "asa-manager.db");
Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? builder.Environment.ContentRootPath);
string connectionString = $"Data Source={databasePath}";

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddDbContextFactory<AppDbContext>(
    options => options.UseSqlite(connectionString),
    ServiceLifetime.Scoped);

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        options.User.RequireUniqueEmail = true;
        options.Password.RequireDigit = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 5;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<ServerMonitorService>();
builder.Services.AddHostedService(services => services.GetRequiredService<ServerMonitorService>());
builder.Services.AddSingleton<PlayerCountMonitorService>();
builder.Services.AddHostedService(services => services.GetRequiredService<PlayerCountMonitorService>());
builder.Services.AddSingleton<StateHubPublisherService>();
builder.Services.AddHostedService(services => services.GetRequiredService<StateHubPublisherService>());
builder.Services.AddScoped<ActionMappingService>();
builder.Services.AddScoped<LogsService>();
builder.Services.AddScoped<ManagerService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<EmailSettingsService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<GameConfigService>();
builder.Services.AddScoped<InstallStateService>();
builder.Services.AddScoped<ProtonConfigService>();
builder.Services.AddScoped<RconService>();
builder.Services.AddScoped<ShellConsoleService>();
builder.Services.AddSingleton<ServerConfigService>();
builder.Services.AddScoped<SystemMetricsService>();

WebApplication app = builder.Build();

if (bootstrapDatabase)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.EnsureDeletedAsync();
    await dbContext.Database.EnsureCreatedAsync();
    AuthService authService = scope.ServiceProvider.GetRequiredService<AuthService>();
    await authService.EnsureDefaultAdminUserAsync();
    return;
}

await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
    AuthService authService = scope.ServiceProvider.GetRequiredService<AuthService>();
    await authService.EnsureDefaultAdminUserAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapPost("/auth/login",
    async ([FromForm] LoginRequest request, SignInManager<ApplicationUser> signInManager, AuthService authService) =>
    {
        Microsoft.AspNetCore.Identity.SignInResult result = await signInManager.PasswordSignInAsync(
            request.Username ?? string.Empty,
            request.Password ?? string.Empty,
            isPersistent: true,
            lockoutOnFailure: false);

        bool mustChangePassword = result.Succeeded &&
                                  await authService.MustChangePasswordAsync(request.Username);

        return result.Succeeded
            ? Results.LocalRedirect(mustChangePassword ? "/admin/reset-password?firstLogin=true" : "/admin/dashboard")
            : Results.LocalRedirect("/admin/login?error=Invalid%20username%20or%20password.");
    })
    .DisableAntiforgery();

app.MapPost("/auth/logout",
    async (SignInManager<ApplicationUser> signInManager) =>
    {
        await signInManager.SignOutAsync();
        return Results.LocalRedirect("/admin/login?message=Logged%20out.");
    })
    .DisableAntiforgery();

app.MapStaticAssets();
app.MapControllers();
app.MapHub<AsaStateHub>(AsaStateHubConstants.Route);
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

internal sealed record LoginRequest(string? Username, string? Password);
