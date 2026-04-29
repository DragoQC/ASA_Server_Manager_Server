namespace asa_server_node_api.Models.Ui;

public sealed record ToastItem(
    Guid Id,
    ToastLevel Level,
    string Tag,
    string Message,
    DateTimeOffset CreatedAtUtc);
