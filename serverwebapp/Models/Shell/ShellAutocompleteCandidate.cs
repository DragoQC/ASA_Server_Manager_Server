namespace AsaServerManager.Web.Models.Shell;

internal sealed record ShellAutocompleteCandidate(
    string Name,
    string Completion,
    string Suggestion);
