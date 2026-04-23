namespace asa_server_node_api.Models.Shell;

internal sealed record ShellAutocompleteCandidate(
    string Name,
    string Completion,
    string Suggestion);
