namespace AsaServerManager.Web.Models.Shell;

public sealed record ShellAutocompleteResult(
    string Command,
    IReadOnlyList<string> Suggestions);
