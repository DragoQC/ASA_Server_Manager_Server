using Microsoft.AspNetCore.Components;

namespace asa_server_node_api.Services;

public sealed class HelpDialogService
{
    public event Action? Changed;

    public bool IsOpen { get; private set; }
    public string Title { get; private set; } = "";
    public string? Description { get; private set; }
    public RenderFragment? DescriptionContent { get; private set; }

    public void Show(string title, string? description, RenderFragment? descriptionContent)
    {
        Title = title;
        Description = description;
        DescriptionContent = descriptionContent;
        IsOpen = true;
        Changed?.Invoke();
    }

    public void Close()
    {
        IsOpen = false;
        Changed?.Invoke();
    }
}
