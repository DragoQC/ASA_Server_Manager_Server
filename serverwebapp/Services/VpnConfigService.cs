using System.Net.Http.Json;
using AsaServerManager.Web.Constants;
using AsaServerManager.Web.Contracts.Api.Admin;

namespace AsaServerManager.Web.Services;

public sealed class VpnConfigService
{
    private readonly ServerConfigService _serverConfigService;
    private readonly InstallStateService _installStateService;
    private readonly AuthService _authService;
    private readonly IHttpClientFactory _httpClientFactory;

    public VpnConfigService(
        ServerConfigService serverConfigService,
        InstallStateService installStateService,
        AuthService authService,
        IHttpClientFactory httpClientFactory)
    {
        _serverConfigService = serverConfigService;
        _installStateService = installStateService;
        _authService = authService;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<InviteRemoteServerResponse> SaveInviteAsync(
        InviteRemoteServerRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);

            string content = ValidateWg0Config(request.Wg0Config);
            string controlApiKey = RequireSingleLine(request.RemoteApiKey, "Remote API key");

            Directory.CreateDirectory(InstallStateConstants.VpnRootPath);
            await File.WriteAllTextAsync(InstallStateConstants.WireGuardConfigFilePath, content, cancellationToken);
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(
                    InstallStateConstants.WireGuardConfigFilePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            await _authService.SaveControlApiKeyAsync(controlApiKey, cancellationToken);
            string clusterId = await _serverConfigService.UpdateClusterIdAsync(request.ClusterId, cancellationToken);
            string wireGuardMessage = await _installStateService.EnableAndRestartWireGuardAsync(cancellationToken);
            string restartMessage = await _installStateService.RestartAsaIfRunningAsync(cancellationToken);

            return new InviteRemoteServerResponse(
                true,
                string.IsNullOrWhiteSpace(clusterId)
                    ? $"WireGuard client configuration saved. Cluster ID cleared. {wireGuardMessage} {restartMessage}"
                    : $"WireGuard client configuration saved. Cluster ID set to {clusterId}. {wireGuardMessage} {restartMessage}",
                InstallStateConstants.WireGuardConfigFilePath);
        }
        catch (ArgumentException ex)
        {
            return new InviteRemoteServerResponse(false, ex.Message, null);
        }
        catch (InvalidOperationException ex)
        {
            return new InviteRemoteServerResponse(false, ex.Message, null);
        }
    }

    public async Task<InviteRemoteServerResponse> ClaimInviteAsync(
        string? inviteUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(inviteUrl))
            {
                throw new ArgumentException("Invite URL is required.");
            }

            if (!Uri.TryCreate(inviteUrl.Trim(), UriKind.Absolute, out Uri? inviteUri) ||
                (inviteUri.Scheme != Uri.UriSchemeHttp && inviteUri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("Invite URL must be a valid absolute http or https URL.");
            }

            HttpClient client = _httpClientFactory.CreateClient();
            InviteRemoteServerRequest? request = await client.GetFromJsonAsync<InviteRemoteServerRequest>(inviteUri, cancellationToken);
            if (request is null)
            {
                throw new InvalidOperationException("The remote invite endpoint returned no configuration.");
            }

            return await SaveInviteAsync(request, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return new InviteRemoteServerResponse(false, ex.Message, null);
        }
        catch (InvalidOperationException ex)
        {
            return new InviteRemoteServerResponse(false, ex.Message, null);
        }
        catch (HttpRequestException ex)
        {
            return new InviteRemoteServerResponse(false, ex.Message, null);
        }
    }

    public async Task<string?> LoadConfigContentAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(InstallStateConstants.WireGuardConfigFilePath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(InstallStateConstants.WireGuardConfigFilePath, cancellationToken);
    }

    private static string ValidateWg0Config(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("wg0.conf is required.");
        }

        string normalizedContent = content.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

        if (normalizedContent.IndexOf('\0') >= 0)
        {
            throw new ArgumentException("wg0.conf contains invalid characters.");
        }

        if (!normalizedContent.Contains("[Interface]", StringComparison.Ordinal) ||
            !normalizedContent.Contains("[Peer]", StringComparison.Ordinal))
        {
            throw new ArgumentException("wg0.conf must contain [Interface] and [Peer] sections.");
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
