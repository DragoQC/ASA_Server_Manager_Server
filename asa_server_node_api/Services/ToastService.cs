using asa_server_node_api.Models.Ui;

namespace asa_server_node_api.Services;

public sealed class ToastService
{
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(1.8);
    private readonly List<ToastItem> _items = [];
    private readonly object _sync = new();

    public event Action? Changed;

    public IReadOnlyList<ToastItem> Items
    {
        get
        {
            lock (_sync)
            {
                return _items.ToArray();
            }
        }
    }

    public void Show(string message, ToastLevel level = ToastLevel.Info, string? tag = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        ToastItem item = new(
            Guid.NewGuid(),
            level,
            string.IsNullOrWhiteSpace(tag) ? BuildDefaultTag(level) : tag.Trim(),
            message.Trim(),
            DateTimeOffset.UtcNow);

        lock (_sync)
        {
            _items.Add(item);
        }

        Changed?.Invoke();
        _ = DismissLaterAsync(item.Id);
    }

    public void ShowSuccess(string message, string? tag = null)
    {
        Show(message, ToastLevel.Success, tag);
    }

    public void ShowError(string message, string? tag = null)
    {
        Show(message, ToastLevel.Error, tag);
    }

    public void ShowInfo(string message, string? tag = null)
    {
        Show(message, ToastLevel.Info, tag);
    }

    public void Dismiss(Guid id)
    {
        bool removed;

        lock (_sync)
        {
            removed = _items.RemoveAll(item => item.Id == id) > 0;
        }

        if (removed)
        {
            Changed?.Invoke();
        }
    }

    private async Task DismissLaterAsync(Guid id)
    {
        try
        {
            await Task.Delay(DefaultDuration);
            Dismiss(id);
        }
        catch
        {
        }
    }

    private static string BuildDefaultTag(ToastLevel level)
    {
        return level switch
        {
            ToastLevel.Success => "Success",
            ToastLevel.Error => "Error",
            _ => "Info"
        };
    }
}
