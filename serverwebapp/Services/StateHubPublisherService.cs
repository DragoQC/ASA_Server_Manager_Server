using AsaServerManager.Web.Constants;
using AsaServerManager.Web.Hubs;
using AsaServerManager.Web.Models.Asa;
using AsaServerManager.Web.Models.Players;
using Microsoft.AspNetCore.SignalR;

namespace AsaServerManager.Web.Services;

public sealed class StateHubPublisherService(
    ServerMonitorService serverMonitorService,
    PlayerCountMonitorService playerCountMonitorService,
    IHubContext<AsaStateHub> hubContext) : IHostedService
{
    private readonly ServerMonitorService _serverMonitorService = serverMonitorService;
    private readonly PlayerCountMonitorService _playerCountMonitorService = playerCountMonitorService;
    private readonly IHubContext<AsaStateHub> _hubContext = hubContext;
    private PlayerCountSnapshot _lastPlayerCountSnapshot = PlayerCountSnapshot.Default();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _serverMonitorService.StatusChanged += OnStatusChanged;
        _playerCountMonitorService.Changed += OnPlayerCountChanged;
        _lastPlayerCountSnapshot = _playerCountMonitorService.GetSnapshot();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _serverMonitorService.StatusChanged -= OnStatusChanged;
        _playerCountMonitorService.Changed -= OnPlayerCountChanged;
        return Task.CompletedTask;
    }

    private void OnStatusChanged()
    {
        _ = BroadcastAsync();
    }

    private void OnPlayerCountChanged()
    {
        PlayerCountSnapshot snapshot = _playerCountMonitorService.GetSnapshot();
        if (snapshot == _lastPlayerCountSnapshot)
        {
            return;
        }

        _lastPlayerCountSnapshot = snapshot;
        _ = BroadcastPlayerCountAsync(snapshot);
    }

    private async Task BroadcastAsync()
    {
        AsaServiceStatus status = AsaServiceStatusFactory.FromSnapshot(_serverMonitorService.GetSnapshot());
        await _hubContext.Clients.All.SendAsync(AsaStateHubConstants.StateUpdatedMethod, status);
    }

    private Task BroadcastPlayerCountAsync(PlayerCountSnapshot snapshot)
    {
        return _hubContext.Clients.All.SendAsync(AsaStateHubConstants.PlayerCountUpdatedMethod, snapshot);
    }
}
