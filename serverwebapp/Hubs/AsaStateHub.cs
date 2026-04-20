using AsaServerManager.Web.Constants;
using AsaServerManager.Web.Models.Asa;
using AsaServerManager.Web.Models.Players;
using AsaServerManager.Web.Services;
using Microsoft.AspNetCore.SignalR;

namespace AsaServerManager.Web.Hubs;

public sealed class AsaStateHub(ServerMonitorService serverMonitorService, PlayerCountMonitorService playerCountMonitorService) : Hub
{
    private readonly ServerMonitorService _serverMonitorService = serverMonitorService;
    private readonly PlayerCountMonitorService _playerCountMonitorService = playerCountMonitorService;

    public override async Task OnConnectedAsync()
    {
        AsaServiceStatus status = AsaServiceStatusFactory.FromSnapshot(_serverMonitorService.GetSnapshot());
        PlayerCountSnapshot playerCountSnapshot = _playerCountMonitorService.GetSnapshot();
        await Clients.Caller.SendAsync(AsaStateHubConstants.StateUpdatedMethod, status);
        await Clients.Caller.SendAsync(AsaStateHubConstants.PlayerCountUpdatedMethod, playerCountSnapshot.CurrentPlayers);
        await base.OnConnectedAsync();
    }
}
