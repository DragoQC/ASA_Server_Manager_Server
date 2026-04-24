using asa_server_node_api.Components;
using asa_server_node_api.Constants;
using asa_server_node_api.Data;
using asa_server_node_api.Hubs;
using asa_server_node_api.Services;
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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("asa-server-node-api/1.0");
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
builder.Services.AddSingleton<AdminHostMetricsMonitorService>();
builder.Services.AddHostedService(services => services.GetRequiredService<AdminHostMetricsMonitorService>());
builder.Services.AddSingleton<AdminInstallStateHubService>();
builder.Services.AddSingleton<StateHubPublisherService>();
builder.Services.AddHostedService(services => services.GetRequiredService<StateHubPublisherService>());
builder.Services.AddSingleton<AdminStateHubPublisherService>();
builder.Services.AddHostedService(services => services.GetRequiredService<AdminStateHubPublisherService>());
builder.Services.AddScoped<ActionMappingService>();
builder.Services.AddScoped<ConsoleLogService>();
builder.Services.AddScoped<LogsService>();
builder.Services.AddScoped<ManagerService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<BackupExportService>();
builder.Services.AddScoped<ClusterClientInstallService>();
builder.Services.AddScoped<GameConfigService>();
builder.Services.AddScoped<InstallStateService>();
builder.Services.AddScoped<ProtonInstallService>();
builder.Services.AddScoped<ProtonConfigService>();
builder.Services.AddScoped<RconService>();
builder.Services.AddScoped<ShellConsoleService>();
builder.Services.AddSingleton<ServerConfigService>();
builder.Services.AddScoped<SystemMetricsService>();
builder.Services.AddScoped<UploadedFileService>();
builder.Services.AddScoped<VpnConfigService>();
builder.Services.AddScoped<NfsConfigService>();

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

app.MapGet("/admin/settings/export/download/{format}",
    (string format, BackupExportService backupExportService) =>
    {
        asa_server_node_api.Models.BackupArchiveInfo? archive = backupExportService.GetLatestArchive(format);
        if (archive is null)
        {
            return Results.NotFound();
        }

        string contentType = string.Equals(format, "zip", StringComparison.OrdinalIgnoreCase)
            ? "application/zip"
            : "application/gzip";

        return Results.File(
            archive.FilePath,
            contentType,
            fileDownloadName: archive.FileName,
            enableRangeProcessing: true);
    })
    .RequireAuthorization();

app.MapStaticAssets();
app.MapControllers();
app.MapHub<AsaStateHub>(AsaStateHubConstants.Route);
app.MapHub<AdminStateHub>(AdminStateHubConstants.Route);
app.MapHub<AdminInstallStateHub>(AdminInstallStateHubConstants.Route);
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

internal sealed record LoginRequest(string? Username, string? Password);
