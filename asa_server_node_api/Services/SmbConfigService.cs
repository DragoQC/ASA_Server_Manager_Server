using System.Net.Http.Json;
using asa_server_node_api.Constants;
using asa_server_node_api.Contracts.Api.Cluster;

namespace asa_server_node_api.Services;

public sealed class SmbConfigService(
    ServerConfigService serverConfigService,
    InstallStateService installStateService,
    IHttpClientFactory httpClientFactory)
{
    private readonly ServerConfigService _serverConfigService = serverConfigService;
    private readonly InstallStateService _installStateService = installStateService;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    public async Task<string> ClaimInviteAsync(string? inviteUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(inviteUrl))
        {
            throw new ArgumentException("SMB invite URL is required.");
        }

        if (!Uri.TryCreate(inviteUrl.Trim(), UriKind.Absolute, out Uri? inviteUri) ||
            (inviteUri.Scheme != Uri.UriSchemeHttp && inviteUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("SMB invite URL must be a valid absolute http or https URL.");
        }

        HttpClient client = _httpClientFactory.CreateClient();
        SmbShareInviteResponse? response = await client.GetFromJsonAsync<SmbShareInviteResponse>(inviteUri, cancellationToken);
        if (response is null)
        {
            throw new InvalidOperationException("The remote SMB invite endpoint returned no configuration.");
        }

        string sharePath = RequireSingleLine(response.SharePath, "SMB share path");
        string mountPath = RequireSingleLine(response.MountPath, "SMB mount path");
        string clientConfig = ValidateClientConfig(response.ClientConfig, sharePath, mountPath);

        Directory.CreateDirectory(InstallStateConstants.SmbRootPath);
        await File.WriteAllTextAsync(InstallStateConstants.SmbClientConfigFilePath, clientConfig, cancellationToken);

        string clusterDir = await _serverConfigService.UpdateClusterDirAsync(mountPath, cancellationToken);
        string fstabMessage = await _installStateService.ApplySmbClientConfigAsync(cancellationToken);
        string restartMessage = await _installStateService.RestartAsaIfRunningAsync(cancellationToken);

        return $"SMB client configuration saved. Cluster dir set to {clusterDir}. {fstabMessage} {restartMessage}";
    }

    public async Task<string?> LoadConfigContentAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(InstallStateConstants.SmbClientConfigFilePath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(InstallStateConstants.SmbClientConfigFilePath, cancellationToken);
    }

    private static string ValidateClientConfig(string? content, string sharePath, string mountPath)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("SMB client config is required.");
        }

        string normalizedContent = content.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (normalizedContent.IndexOf('\0') >= 0)
        {
            throw new ArgumentException("SMB client config contains invalid characters.");
        }

        string? configLine = normalizedContent
            .Split('\n', StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'));

        if (string.IsNullOrWhiteSpace(configLine))
        {
            throw new ArgumentException("SMB client config must contain a valid mount line.");
        }

        if (!configLine.Contains(sharePath, StringComparison.Ordinal) ||
            !configLine.Contains(mountPath, StringComparison.Ordinal))
        {
            throw new ArgumentException("SMB client config does not match the invite response.");
        }

        return normalizedContent + "\n";
    }

    private static string RequireSingleLine(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{label} is required.");
        }

        string trimmedValue = value.Trim();
        if (trimmedValue.IndexOfAny(['\0', '\r', '\n', '\u001a']) >= 0)
        {
            throw new ArgumentException($"{label} contains invalid characters.");
        }

        return trimmedValue;
    }
}
