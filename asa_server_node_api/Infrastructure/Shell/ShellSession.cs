using System.Diagnostics;
using System.Text;
using asa_server_node_api.Constants;
using asa_server_node_api.Models.Shell;

namespace asa_server_node_api.Infrastructure.Shell;

internal sealed class ShellSession : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly StringBuilder _transcript = new();
    private readonly Action _notifyChanged;
    private readonly string _requestedWorkingDirectory;

    private Process? _process;
    private bool _suppressExitMessage;
    private string _workingDirectory = "/";
    private bool _workingDirectoryExists;
    private bool _isBusy;
    private bool _disposed;

    public ShellSession(string requestedWorkingDirectory, Action notifyChanged)
    {
        _requestedWorkingDirectory = requestedWorkingDirectory;
        _notifyChanged = notifyChanged;
        StartedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = StartedAtUtc;

        StartProcess();
    }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public ShellConsoleSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new ShellConsoleSnapshot(
                _requestedWorkingDirectory,
                _workingDirectory,
                _workingDirectoryExists,
                _process is { HasExited: false },
                _isBusy,
                _transcript.ToString(),
                StartedAtUtc,
                UpdatedAtUtc);
        }
    }

    public ShellAutocompleteResult Autocomplete(string input)
    {
        string command = input ?? string.Empty;
        string trimmedEnd = command.TrimEnd();
        if (string.IsNullOrWhiteSpace(trimmedEnd))
        {
            return new ShellAutocompleteResult(command, Array.Empty<string>());
        }

        int tokenStart = trimmedEnd.LastIndexOf(' ') + 1;
        string token = trimmedEnd[tokenStart..];
        if (string.IsNullOrWhiteSpace(token))
        {
            return new ShellAutocompleteResult(command, Array.Empty<string>());
        }

        int slashIndex = token.LastIndexOf('/');
        string displayDirectoryPart = slashIndex >= 0 ? token[..(slashIndex + 1)] : string.Empty;
        string typedNamePart = slashIndex >= 0 ? token[(slashIndex + 1)..] : token;
        string resolvedDirectory = ResolveAutocompleteDirectory(displayDirectoryPart);

        if (!Directory.Exists(resolvedDirectory))
        {
            return new ShellAutocompleteResult(command, Array.Empty<string>());
        }

        List<ShellAutocompleteCandidate> candidates = Directory.EnumerateFileSystemEntries(resolvedDirectory)
            .Select(path =>
            {
                string name = Path.GetFileName(path);
                bool isDirectory = Directory.Exists(path);
                return new ShellAutocompleteCandidate(
                    name,
                    $"{displayDirectoryPart}{name}{(isDirectory ? "/" : string.Empty)}",
                    isDirectory ? $"{name}/" : name);
            })
            .Where(entry => entry.Name.StartsWith(typedNamePart, StringComparison.Ordinal))
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();

        if (candidates.Count == 0)
        {
            return new ShellAutocompleteResult(command, Array.Empty<string>());
        }

        string sharedPrefix = LongestCommonPrefix(candidates.Select(candidate => candidate.Name));
        string replacement = typedNamePart.Length < sharedPrefix.Length
            ? $"{displayDirectoryPart}{sharedPrefix}"
            : candidates.Count == 1
                ? candidates[0].Completion
                : token;

        string updatedCommand = $"{command[..tokenStart]}{replacement}";
        if (candidates.Count == 1 && !updatedCommand.EndsWith('/'))
        {
            updatedCommand += " ";
        }

        return new ShellAutocompleteResult(updatedCommand, candidates.Select(candidate => candidate.Suggestion).ToArray());
    }

    public async Task ExecuteAsync(string command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        await _commandLock.WaitAsync(cancellationToken);
        try
        {
            EnsureRunning();

            lock (_sync)
            {
                _isBusy = true;
                AppendLineUnlocked($"$ {command}");
            }

            await _process!.StandardInput.WriteLineAsync(command);
            await _process.StandardInput.WriteLineAsync($"printf '{ShellConsoleConstants.WorkingDirectoryMarker}%s\\n' \"$PWD\"");
            await _process.StandardInput.WriteLineAsync($"printf '\\n{ShellConsoleConstants.CompletionMarker}%s\\n' \"$?\"");
            await _process.StandardInput.FlushAsync(cancellationToken);
        }
        finally
        {
            _commandLock.Release();
            _notifyChanged();
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _transcript.Clear();
            UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        _notifyChanged();
    }

    public async Task RestartAsync()
    {
        await _commandLock.WaitAsync();
        try
        {
            await StopProcessAsync();

            lock (_sync)
            {
                _transcript.Clear();
                _isBusy = false;
                UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            StartProcess();
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private void StartProcess()
    {
        string fallbackDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _workingDirectoryExists = Directory.Exists(_requestedWorkingDirectory);
        _workingDirectory = _workingDirectoryExists ? _requestedWorkingDirectory : fallbackDirectory;

        Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = "--noprofile --norc",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _workingDirectory
            },
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, args) => HandleOutput(args.Data, false);
        process.ErrorDataReceived += (_, args) => HandleOutput(args.Data, true);
        process.Exited += (_, _) =>
        {
            lock (_sync)
            {
                _isBusy = false;
                if (!_suppressExitMessage)
                {
                    AppendLineUnlocked("Shell session exited.");
                }
            }

            _notifyChanged();
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process = process;

        process.StandardInput.WriteLine("export TERM=xterm-256color COLUMNS=120 LINES=40");
        process.StandardInput.WriteLine("alias ls='ls -C --color=auto'");
        process.StandardInput.WriteLine("alias ll='ls -lah --color=auto'");
        process.StandardInput.Flush();

        _suppressExitMessage = false;

        lock (_sync)
        {
            AppendLineUnlocked($"Shell started in {_workingDirectory}");
            if (!_workingDirectoryExists)
            {
                AppendLineUnlocked($"Requested directory '{_requestedWorkingDirectory}' was not found. Using '{_workingDirectory}' instead.");
            }
        }

        _notifyChanged();
    }

    private void HandleOutput(string? line, bool isError)
    {
        if (line is null)
        {
            return;
        }

        lock (_sync)
        {
            if (line.StartsWith(ShellConsoleConstants.WorkingDirectoryMarker, StringComparison.Ordinal))
            {
                _workingDirectory = line[ShellConsoleConstants.WorkingDirectoryMarker.Length..];
                UpdatedAtUtc = DateTimeOffset.UtcNow;
                return;
            }

            if (line.StartsWith(ShellConsoleConstants.CompletionMarker, StringComparison.Ordinal))
            {
                _isBusy = false;
                AppendLineUnlocked($"Exit code: {line[ShellConsoleConstants.CompletionMarker.Length..]}");
            }
            else
            {
                AppendLineUnlocked(isError ? $"! {line}" : line);
            }
        }

        _notifyChanged();
    }

    private void EnsureRunning()
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        StartProcess();
    }

    private void AppendLineUnlocked(string line)
    {
        _transcript.AppendLine(line);
        UpdatedAtUtc = DateTimeOffset.UtcNow;

        if (_transcript.Length > ShellConsoleConstants.MaxTranscriptChars)
        {
            _transcript.Remove(0, _transcript.Length - ShellConsoleConstants.MaxTranscriptChars);
        }
    }

    private string ResolveAutocompleteDirectory(string displayDirectoryPart)
    {
        if (string.IsNullOrWhiteSpace(displayDirectoryPart))
        {
            return _workingDirectory;
        }

        string homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (displayDirectoryPart.StartsWith("~/", StringComparison.Ordinal))
        {
            return Path.GetFullPath(Path.Combine(homeDirectory, displayDirectoryPart[2..]));
        }

        if (displayDirectoryPart == "~/")
        {
            return homeDirectory;
        }

        if (Path.IsPathRooted(displayDirectoryPart))
        {
            return Path.GetFullPath(displayDirectoryPart);
        }

        return Path.GetFullPath(Path.Combine(_workingDirectory, displayDirectoryPart));
    }

    private static string LongestCommonPrefix(IEnumerable<string> values)
    {
        using IEnumerator<string> enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return string.Empty;
        }

        string prefix = enumerator.Current;
        while (enumerator.MoveNext() && prefix.Length > 0)
        {
            string current = enumerator.Current;
            int index = 0;
            int max = Math.Min(prefix.Length, current.Length);
            while (index < max && prefix[index] == current[index])
            {
                index++;
            }

            prefix = prefix[..index];
        }

        return prefix;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            await StopProcessAsync();
        }
        finally
        {
            _process?.Dispose();
            _commandLock.Dispose();
        }
    }

    private async Task StopProcessAsync()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _suppressExitMessage = true;
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        catch
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }
}
