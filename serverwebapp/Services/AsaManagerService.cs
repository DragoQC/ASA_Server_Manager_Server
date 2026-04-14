using AsaServerManager.Web.Models.Asa;

namespace AsaServerManager.Web.Services;

public sealed class AsaManagerService(InstallStateService installStateService) : IAsyncDisposable
{
    private readonly InstallStateService _installStateService = installStateService;
    private CancellationTokenSource? _pollingCancellationTokenSource;
    private Task? _pollingTask;
    private bool _isRefreshing;

    public event Action? Changed;

    public AsaServiceStatus CurrentStatus { get; private set; } = AsaServiceStatus.Unknown();

    public bool ShouldPromptForRestart => CurrentStatus.ShouldPromptForRestart;

    public async Task EnsureStartedAsync()
    {
        if (_pollingTask is not null)
        {
            return;
        }

        await RefreshAsync();

        _pollingCancellationTokenSource = new CancellationTokenSource();
        _pollingTask = RunPollingLoopAsync(_pollingCancellationTokenSource.Token);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;

        try
        {
            CurrentStatus = await _installStateService.GetAsaServiceStatusAsync(cancellationToken);
        }
        catch
        {
            CurrentStatus = AsaServiceStatus.Unknown("Unavailable");
        }
        finally
        {
            _isRefreshing = false;
            Changed?.Invoke();
        }
    }

    public async Task<string> EnableAsync(CancellationToken cancellationToken = default)
    {
        string message = await _installStateService.EnableAsaServiceAsync(cancellationToken);
        await RefreshAsync(cancellationToken);
        return message;
    }

    public async Task<string> StartAsync(CancellationToken cancellationToken = default)
    {
        string message = await _installStateService.StartAsaServiceAsync(cancellationToken);
        await RefreshAsync(cancellationToken);
        return message;
    }

    public async Task<string> StopAsync(CancellationToken cancellationToken = default)
    {
        string message = await _installStateService.StopAsaServiceAsync(cancellationToken);
        await RefreshAsync(cancellationToken);
        return message;
    }

    public async Task<string> RestartAsync(CancellationToken cancellationToken = default)
    {
        string message = await _installStateService.RestartAsaServiceAsync(cancellationToken);
        await RefreshAsync(cancellationToken);
        return message;
    }

    private async Task RunPollingLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(2));

            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_pollingCancellationTokenSource is not null)
        {
            _pollingCancellationTokenSource.Cancel();
            _pollingCancellationTokenSource.Dispose();
            _pollingCancellationTokenSource = null;
        }

        if (_pollingTask is not null)
        {
            try
            {
                await _pollingTask;
            }
            catch (OperationCanceledException)
            {
            }

            _pollingTask = null;
        }
    }
}
