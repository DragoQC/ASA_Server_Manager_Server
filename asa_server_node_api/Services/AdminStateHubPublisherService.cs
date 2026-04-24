using asa_server_node_api.Constants;
using asa_server_node_api.Hubs;
using asa_server_node_api.Models.Admin;
using Microsoft.AspNetCore.SignalR;

namespace asa_server_node_api.Services;

public sealed class AdminStateHubPublisherService(
    AdminHostMetricsMonitorService adminHostMetricsMonitorService,
    IHubContext<AdminStateHub> hubContext) : IHostedService
{
    private readonly AdminHostMetricsMonitorService _adminHostMetricsMonitorService = adminHostMetricsMonitorService;
    private readonly IHubContext<AdminStateHub> _hubContext = hubContext;
    private AdminHostMetricsSnapshot _lastSnapshot = AdminHostMetricsSnapshot.Default();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _adminHostMetricsMonitorService.Changed += OnChanged;
        _lastSnapshot = _adminHostMetricsMonitorService.GetSnapshot();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _adminHostMetricsMonitorService.Changed -= OnChanged;
        return Task.CompletedTask;
    }

    private void OnChanged()
    {
        AdminHostMetricsSnapshot snapshot = _adminHostMetricsMonitorService.GetSnapshot();
        if (snapshot == _lastSnapshot)
        {
            return;
        }

        _lastSnapshot = snapshot;
        _ = _hubContext.Clients.All.SendAsync(AdminStateHubConstants.HostMetricsUpdatedMethod, snapshot);
    }
}
