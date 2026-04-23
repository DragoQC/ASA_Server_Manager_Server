using Microsoft.JSInterop;

namespace asa_server_node_api.Services;

public sealed class ConsoleLogService(IJSRuntime jsRuntime)
{
    private readonly IJSRuntime _jsRuntime = jsRuntime;

    public ValueTask LogAsync(params object?[] args)
    {
        return _jsRuntime.InvokeVoidAsync("console.log", args);
    }

    public ValueTask WarnAsync(params object?[] args)
    {
        return _jsRuntime.InvokeVoidAsync("console.warn", args);
    }

    public ValueTask ErrorAsync(params object?[] args)
    {
        return _jsRuntime.InvokeVoidAsync("console.error", args);
    }
}
