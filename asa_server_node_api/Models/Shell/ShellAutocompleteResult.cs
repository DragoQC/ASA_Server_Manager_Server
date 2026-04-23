namespace asa_server_node_api.Models.Shell;

public sealed record ShellAutocompleteResult(
    string Command,
    IReadOnlyList<string> Suggestions);
