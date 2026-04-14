using AsaServerManager.Web.Constants;
using AsaServerManager.Web.Hubs;
using AsaServerManager.Web.Models.Asa;
using Microsoft.AspNetCore.SignalR;

namespace AsaServerManager.Web.Services;

public sealed class AsaStateHubPublisher(
    AsaServerMonitor asaServerMonitor,
    IHubContext<AsaStateHub> hubContext) : IHostedService
{
    private readonly AsaServerMonitor _asaServerMonitor = asaServerMonitor;
    private readonly IHubContext<AsaStateHub> _hubContext = hubContext;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _asaServerMonitor.StatusChanged += OnStatusChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _asaServerMonitor.StatusChanged -= OnStatusChanged;
        return Task.CompletedTask;
    }

    private void OnStatusChanged()
    {
        _ = BroadcastAsync();
    }

    private async Task BroadcastAsync()
    {
        AsaServiceStatus status = AsaServiceStatusFactory.FromSnapshot(_asaServerMonitor.GetSnapshot());
        await _hubContext.Clients.All.SendAsync(AsaStateHubConstants.StateUpdatedMethod, status);
    }
}
