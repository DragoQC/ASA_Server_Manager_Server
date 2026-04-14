using AsaServerManager.Web.Constants;
using AsaServerManager.Web.Models.Asa;
using AsaServerManager.Web.Services;
using Microsoft.AspNetCore.SignalR;

namespace AsaServerManager.Web.Hubs;

public sealed class AsaStateHub(AsaServerMonitor asaServerMonitor) : Hub
{
    private readonly AsaServerMonitor _asaServerMonitor = asaServerMonitor;

    public override async Task OnConnectedAsync()
    {
        AsaServiceStatus status = AsaServiceStatusFactory.FromSnapshot(_asaServerMonitor.GetSnapshot());
        await Clients.Caller.SendAsync(AsaStateHubConstants.StateUpdatedMethod, status);
        await base.OnConnectedAsync();
    }
}
