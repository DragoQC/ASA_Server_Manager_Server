using asa_server_node_api.Models;
using asa_server_node_api.Models.Players;
using asa_server_node_api.Models.Rcon;

namespace asa_server_node_api.Services;

public sealed class PlayerCountMonitorService(
    ServerMonitorService serverMonitorService,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<PlayerCountMonitorService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private readonly ServerMonitorService _serverMonitorService = serverMonitorService;
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly ILogger<PlayerCountMonitorService> _logger = logger;
    private volatile PlayerCountSnapshot _current = PlayerCountSnapshot.Default();

    public event Action? Changed;

    public PlayerCountSnapshot GetSnapshot() => _current;

    public Task RefreshNowAsync(CancellationToken cancellationToken = default) => RefreshAsync(cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshAsync(stoppingToken);

        using PeriodicTimer timer = new(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshAsync(stoppingToken);
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        PlayerCountSnapshot snapshot = await ProbeAsync(cancellationToken);
        _current = snapshot;
        Changed?.Invoke();
    }

    private async Task<PlayerCountSnapshot> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            AsaServerStatusSnapshot asaStatus = _serverMonitorService.GetSnapshot();

            await using AsyncServiceScope scope = _serviceScopeFactory.CreateAsyncScope();
            ServerConfigService serverConfigService = scope.ServiceProvider.GetRequiredService<ServerConfigService>();
            GameConfigService gameConfigService = scope.ServiceProvider.GetRequiredService<GameConfigService>();
            RconService rconService = scope.ServiceProvider.GetRequiredService<RconService>();

            int maxPlayers = await serverConfigService.GetMaxPlayersAsync(cancellationToken);

            if (!asaStatus.IsRunning)
            {
                return new PlayerCountSnapshot(
                    CurrentPlayers: 0,
                    MaxPlayers: maxPlayers,
                    StatusLabel: asaStatus.StatusLabel,
                    Message: "ASA is not running.",
                    UpdatedAtUtc: DateTimeOffset.UtcNow);
            }

            if (!gameConfigService.HasGameUserSettingsIniFile())
            {
                return new PlayerCountSnapshot(
                    CurrentPlayers: 0,
                    MaxPlayers: maxPlayers,
                    StatusLabel: "Missing",
                    Message: "GameUserSettings.ini missing.",
                    UpdatedAtUtc: DateTimeOffset.UtcNow);
            }

            RconSettings rconSettings = await rconService.GetSettingsAsync(cancellationToken);
            RconStatus rconStatus = await rconService.GetStatusAsync(cancellationToken);
            if (!rconStatus.CanExecute(rconSettings))
            {
                return new PlayerCountSnapshot(
                    CurrentPlayers: 0,
                    MaxPlayers: maxPlayers,
                    StatusLabel: rconStatus.StateLabel,
                    Message: rconStatus.Message,
                    UpdatedAtUtc: DateTimeOffset.UtcNow);
            }

            int currentPlayers = await rconService.GetOnlinePlayerCountAsync(cancellationToken);

            return new PlayerCountSnapshot(
                CurrentPlayers: currentPlayers,
                MaxPlayers: maxPlayers,
                StatusLabel: "Running",
                Message: currentPlayers == 1 ? "1 player online." : $"{currentPlayers} players online.",
                UpdatedAtUtc: DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh ASA player count.");

            return new PlayerCountSnapshot(
                CurrentPlayers: 0,
                MaxPlayers: _current.MaxPlayers,
                StatusLabel: "Unavailable",
                Message: ex.Message,
                UpdatedAtUtc: DateTimeOffset.UtcNow);
        }
    }
}
