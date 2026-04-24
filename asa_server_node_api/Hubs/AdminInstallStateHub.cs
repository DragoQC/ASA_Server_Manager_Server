using asa_server_node_api.Constants;
using asa_server_node_api.Services;
using Microsoft.AspNetCore.SignalR;

namespace asa_server_node_api.Hubs;

public sealed class AdminInstallStateHub(
    AdminInstallStateHubService adminInstallStateHubService,
    AuthService authService) : Hub
{
    private readonly AdminInstallStateHubService _adminInstallStateHubService = adminInstallStateHubService;
    private readonly AuthService _authService = authService;

    public override async Task OnConnectedAsync()
    {
        HttpContext? httpContext = Context.GetHttpContext();
        string? apiKey = httpContext?.Request.Headers["X-Api-Key"].FirstOrDefault();
        bool isValid = await _authService.IsApiKeyValidAsync(apiKey, Context.ConnectionAborted);
        if (!isValid)
        {
            Context.Abort();
            return;
        }

        await Clients.Caller.SendAsync(
            AdminInstallStateHubConstants.WorkspaceUpdatedMethod,
            await _adminInstallStateHubService.GetWorkspaceSnapshotAsync(Context.ConnectionAborted),
            Context.ConnectionAborted);

        await Clients.Caller.SendAsync(
            AdminInstallStateHubConstants.ProgressUpdatedMethod,
            _adminInstallStateHubService.GetProgressSnapshot(),
            Context.ConnectionAborted);

        await base.OnConnectedAsync();
    }
}
