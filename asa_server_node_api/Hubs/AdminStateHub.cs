using asa_server_node_api.Constants;
using asa_server_node_api.Services;
using Microsoft.AspNetCore.SignalR;

namespace asa_server_node_api.Hubs;

public sealed class AdminStateHub(
    AdminHostMetricsMonitorService adminHostMetricsMonitorService,
    AuthService authService) : Hub
{
    private readonly AdminHostMetricsMonitorService _adminHostMetricsMonitorService = adminHostMetricsMonitorService;
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
            AdminStateHubConstants.HostMetricsUpdatedMethod,
            _adminHostMetricsMonitorService.GetSnapshot(),
            Context.ConnectionAborted);

        await base.OnConnectedAsync();
    }
}
