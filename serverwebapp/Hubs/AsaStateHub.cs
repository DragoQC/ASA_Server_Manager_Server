using AsaServerManager.Web.Constants;
using AsaServerManager.Web.Models.Asa;
using AsaServerManager.Web.Services;
using Microsoft.AspNetCore.SignalR;

namespace AsaServerManager.Web.Hubs;

public sealed class AsaStateHub(ServerMonitorService serverMonitorService) : Hub
{
    private readonly ServerMonitorService _serverMonitorService = serverMonitorService;

    public override async Task OnConnectedAsync()
    {
        AsaServiceStatus status = AsaServiceStatusFactory.FromSnapshot(_serverMonitorService.GetSnapshot());
        await Clients.Caller.SendAsync(AsaStateHubConstants.StateUpdatedMethod, status);
        await base.OnConnectedAsync();
    }
}
