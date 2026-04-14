using System.Collections.Concurrent;
using AsaServerManager.Web.Infrastructure.Shell;
using AsaServerManager.Web.Models.Shell;

namespace AsaServerManager.Web.Services;

public sealed class ShellConsoleService(ActionMappingService actionMappingService) : IAsyncDisposable
{
    private readonly ActionMappingService _actionMappingService = actionMappingService;
    private readonly ConcurrentDictionary<string, ShellSession> _sessions = new(StringComparer.Ordinal);

    public event Action<string>? SessionChanged;

    public ShellConsoleSnapshot GetSnapshot(string key, string requestedWorkingDirectory)
    {
        ShellSession session = _sessions.GetOrAdd(key, _ => new ShellSession(requestedWorkingDirectory, () => SessionChanged?.Invoke(key)));
        return session.GetSnapshot();
    }

    public ShellAutocompleteResult Autocomplete(string key, string requestedWorkingDirectory, string input)
    {
        ShellSession session = _sessions.GetOrAdd(key, _ => new ShellSession(requestedWorkingDirectory, () => SessionChanged?.Invoke(key)));
        return session.Autocomplete(input);
    }

    public async Task ExecuteAsync(string key, string requestedWorkingDirectory, string command, CancellationToken cancellationToken = default)
    {
        ShellSession session = _sessions.GetOrAdd(key, _ => new ShellSession(requestedWorkingDirectory, () => SessionChanged?.Invoke(key)));
        Models.Actions.ActionMapping? actionMapping = await _actionMappingService.ResolveAsync(command, cancellationToken);
        if (actionMapping is not null)
        {
            switch (actionMapping.ActionType)
            {
                case "clear-window":
                    session.Clear();
                    SessionChanged?.Invoke(key);
                    return;
            }
        }

        await session.ExecuteAsync(command, cancellationToken);
    }

    public void Clear(string key, string requestedWorkingDirectory)
    {
        ShellSession session = _sessions.GetOrAdd(key, _ => new ShellSession(requestedWorkingDirectory, () => SessionChanged?.Invoke(key)));
        session.Clear();
    }

    public async Task RestartAsync(string key, string requestedWorkingDirectory)
    {
        ShellSession session = _sessions.GetOrAdd(key, _ => new ShellSession(requestedWorkingDirectory, () => SessionChanged?.Invoke(key)));
        await session.RestartAsync();
        SessionChanged?.Invoke(key);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (ShellSession session in _sessions.Values)
        {
            await session.DisposeAsync();
        }

        _sessions.Clear();
    }
}
