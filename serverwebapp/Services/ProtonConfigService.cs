using AsaServerManager.Web.Constants;

namespace AsaServerManager.Web.Services;

public sealed class ProtonConfigService
{
    public async Task<string> LoadVersionAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ProtonConfigConstants.ProtonEnvFilePath))
        {
            return string.Empty;
        }

        string content = await File.ReadAllTextAsync(ProtonConfigConstants.ProtonEnvFilePath, cancellationToken);
        string normalizedContent = NormalizeLineEndings(content);
        string configuredVersion = string.Empty;

        foreach (string line in normalizedContent.Split('\n'))
        {
            string trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith('#'))
            {
                continue;
            }

            int separatorIndex = trimmedLine.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            string key = trimmedLine[..separatorIndex].Trim();
            if (!string.Equals(key, "PROTON_VERSION", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            configuredVersion = Unquote(trimmedLine[(separatorIndex + 1)..].Trim());
            break;
        }

        string expectedContent = NormalizeLineEndings(BuildContent(configuredVersion));
        if (!string.Equals(normalizedContent, expectedContent, StringComparison.Ordinal))
        {
            await SaveVersionAsync(configuredVersion, cancellationToken);
        }

        return configuredVersion;
    }

    public async Task SaveVersionAsync(string protonVersion, CancellationToken cancellationToken = default)
    {
        string? directoryPath = Path.GetDirectoryName(ProtonConfigConstants.ProtonEnvFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await File.WriteAllTextAsync(ProtonConfigConstants.ProtonEnvFilePath, BuildContent(protonVersion), cancellationToken);
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(ProtonConfigConstants.ProtonEnvFilePath))
        {
            File.Delete(ProtonConfigConstants.ProtonEnvFilePath);
        }

        return Task.CompletedTask;
    }

    private static string BuildContent(string protonVersion)
    {
        return $"PROTON_VERSION=\"{Escape(protonVersion)}\"";
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
        {
            return value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
        }

        return value;
    }

    private static string Escape(string value)
    {
        return value.Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
