using asa_server_node_api.Constants;
using asa_server_node_api.Models.Asa;
using asa_server_node_api.Models.Players;
using asa_server_node_api.Services;
using Microsoft.AspNetCore.SignalR;

namespace asa_server_node_api.Hubs;

public sealed class AsaStateHub(
    ServerMonitorService serverMonitorService,
    PlayerCountMonitorService playerCountMonitorService,
    IServiceScopeFactory serviceScopeFactory) : Hub
{
    private readonly ServerMonitorService _serverMonitorService = serverMonitorService;
    private readonly PlayerCountMonitorService _playerCountMonitorService = playerCountMonitorService;
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;

    public override async Task OnConnectedAsync()
    {
        AsaServiceStatus status = AsaServiceStatusFactory.FromSnapshot(_serverMonitorService.GetSnapshot());
        PlayerCountSnapshot playerCountSnapshot = _playerCountMonitorService.GetSnapshot();
        bool canSendRconCommand = await ResolveCanSendRconCommandAsync(Context.ConnectionAborted);
        await Clients.Caller.SendAsync(AsaStateHubConstants.StateUpdatedMethod, status);
        await Clients.Caller.SendAsync(AsaStateHubConstants.PlayerCountUpdatedMethod, playerCountSnapshot.CurrentPlayers);
        await Clients.Caller.SendAsync(AsaStateHubConstants.CanSendRconCommandUpdatedMethod, canSendRconCommand);
        await base.OnConnectedAsync();
    }

    private async Task<bool> ResolveCanSendRconCommandAsync(CancellationToken cancellationToken)
    {
        if (!_serverMonitorService.GetSnapshot().IsRunning)
        {
            return false;
        }

        await using AsyncServiceScope scope = _serviceScopeFactory.CreateAsyncScope();
        RconService rconService = scope.ServiceProvider.GetRequiredService<RconService>();
        return (await rconService.ProbeAsync(cancellationToken)).IsConnected;
    }
}
