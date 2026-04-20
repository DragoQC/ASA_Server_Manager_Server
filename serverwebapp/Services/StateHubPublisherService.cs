using AsaServerManager.Web.Constants;
using AsaServerManager.Web.Hubs;
using AsaServerManager.Web.Models.Asa;
using AsaServerManager.Web.Models.Players;
using Microsoft.AspNetCore.SignalR;

namespace AsaServerManager.Web.Services;

public sealed class StateHubPublisherService(
    ServerMonitorService serverMonitorService,
    PlayerCountMonitorService playerCountMonitorService,
    IServiceScopeFactory serviceScopeFactory,
    IHubContext<AsaStateHub> hubContext) : IHostedService
{
    private readonly ServerMonitorService _serverMonitorService = serverMonitorService;
    private readonly PlayerCountMonitorService _playerCountMonitorService = playerCountMonitorService;
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly IHubContext<AsaStateHub> _hubContext = hubContext;
    private PlayerCountSnapshot _lastPlayerCountSnapshot = PlayerCountSnapshot.Default();
    private CancellationTokenSource? _rconPollingCancellationTokenSource;
    private Task? _rconPollingTask;
    private bool _lastCanSendRconCommand;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _serverMonitorService.StatusChanged += OnStatusChanged;
        _playerCountMonitorService.Changed += OnPlayerCountChanged;
        _lastPlayerCountSnapshot = _playerCountMonitorService.GetSnapshot();
        _lastCanSendRconCommand = await ResolveCanSendRconCommandAsync(cancellationToken);
        _rconPollingCancellationTokenSource = new CancellationTokenSource();
        _rconPollingTask = RunRconPollingLoopAsync(_rconPollingCancellationTokenSource.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _serverMonitorService.StatusChanged -= OnStatusChanged;
        _playerCountMonitorService.Changed -= OnPlayerCountChanged;

        if (_rconPollingCancellationTokenSource is not null)
        {
            _rconPollingCancellationTokenSource.Cancel();
            _rconPollingCancellationTokenSource.Dispose();
            _rconPollingCancellationTokenSource = null;
        }

        if (_rconPollingTask is not null)
        {
            try
            {
                await _rconPollingTask;
            }
            catch (OperationCanceledException)
            {
            }

            _rconPollingTask = null;
        }
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
        return _hubContext.Clients.All.SendAsync(AsaStateHubConstants.PlayerCountUpdatedMethod, snapshot.CurrentPlayers);
    }

    private async Task RunRconPollingLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(2));

            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                bool canSendRconCommand = await ResolveCanSendRconCommandAsync(cancellationToken);
                if (canSendRconCommand == _lastCanSendRconCommand)
                {
                    continue;
                }

                _lastCanSendRconCommand = canSendRconCommand;
                await _hubContext.Clients.All.SendAsync(AsaStateHubConstants.CanSendRconCommandUpdatedMethod, canSendRconCommand, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
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
