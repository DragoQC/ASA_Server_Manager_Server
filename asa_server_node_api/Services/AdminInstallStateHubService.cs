using asa_server_node_api.Constants;
using asa_server_node_api.Hubs;
using asa_server_node_api.Models.Install;
using Microsoft.AspNetCore.SignalR;

namespace asa_server_node_api.Services;

public sealed class AdminInstallStateHubService(
    IServiceScopeFactory serviceScopeFactory,
    IHubContext<AdminInstallStateHub> hubContext)
{
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly IHubContext<AdminInstallStateHub> _hubContext = hubContext;
    private readonly SemaphoreSlim _workspaceLock = new(1, 1);
    private volatile InstallProgressSnapshot _progressSnapshot = InstallProgressSnapshot.Idle();
    private volatile InstallWorkspaceStatusSnapshot? _workspaceSnapshot;

    public InstallProgressSnapshot GetProgressSnapshot() => _progressSnapshot;

    public async Task<InstallWorkspaceStatusSnapshot> GetWorkspaceSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (_workspaceSnapshot is not null)
        {
            return _workspaceSnapshot;
        }

        await _workspaceLock.WaitAsync(cancellationToken);
        try
        {
            if (_workspaceSnapshot is not null)
            {
                return _workspaceSnapshot;
            }

            InstallWorkspaceStatusSnapshot snapshot = await LoadWorkspaceSnapshotAsync(cancellationToken);
            _workspaceSnapshot = snapshot;
            return snapshot;
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    public async Task BroadcastWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        InstallWorkspaceStatusSnapshot snapshot = await LoadWorkspaceSnapshotAsync(cancellationToken);
        _workspaceSnapshot = snapshot;
        await _hubContext.Clients.All.SendAsync(AdminInstallStateHubConstants.WorkspaceUpdatedMethod, snapshot, cancellationToken);
    }

    public Task BroadcastWorkspaceAsync(InstallWorkspaceSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        InstallWorkspaceStatusSnapshot statusSnapshot = Map(snapshot);
        _workspaceSnapshot = statusSnapshot;
        return _hubContext.Clients.All.SendAsync(AdminInstallStateHubConstants.WorkspaceUpdatedMethod, statusSnapshot, cancellationToken);
    }

    public Task BroadcastProgressAsync(InstallProgressSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        _progressSnapshot = snapshot;
        return _hubContext.Clients.All.SendAsync(AdminInstallStateHubConstants.ProgressUpdatedMethod, snapshot, cancellationToken);
    }

    private async Task<InstallWorkspaceStatusSnapshot> LoadWorkspaceSnapshotAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _serviceScopeFactory.CreateAsyncScope();
        InstallStateService installStateService = scope.ServiceProvider.GetRequiredService<InstallStateService>();
        InstallWorkspaceSnapshot snapshot = await installStateService.LoadAsync(cancellationToken);
        return Map(snapshot);
    }

    private static InstallWorkspaceStatusSnapshot Map(InstallWorkspaceSnapshot snapshot)
    {
        return new InstallWorkspaceStatusSnapshot(
            Proton: snapshot.Proton,
            Steam: snapshot.Steam,
            StartScript: new InstallFileStatusSnapshot(
                snapshot.StartScript.Title,
                snapshot.StartScript.Description,
                snapshot.StartScript.Status,
                snapshot.StartScript.StateLabel,
                snapshot.StartScript.FilePath),
            ServiceFile: new InstallFileStatusSnapshot(
                snapshot.ServiceFile.Title,
                snapshot.ServiceFile.Description,
                snapshot.ServiceFile.Status,
                snapshot.ServiceFile.StateLabel,
                snapshot.ServiceFile.FilePath),
            CheckedAtUtc: snapshot.CheckedAtUtc);
    }
}
