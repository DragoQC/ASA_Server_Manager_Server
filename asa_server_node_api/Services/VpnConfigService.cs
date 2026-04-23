using System.Net.Http.Json;
using asa_server_node_api.Constants;
using asa_server_node_api.Contracts.Api.Admin;

namespace asa_server_node_api.Services;

public sealed class VpnConfigService
{
    private readonly ServerConfigService _serverConfigService;
    private readonly InstallStateService _installStateService;
    private readonly AuthService _authService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<VpnConfigService> _logger;

    public VpnConfigService(
        ServerConfigService serverConfigService,
        InstallStateService installStateService,
        AuthService authService,
        IHttpClientFactory httpClientFactory,
        ILogger<VpnConfigService> logger)
    {
        _serverConfigService = serverConfigService;
        _installStateService = installStateService;
        _authService = authService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<InviteRemoteServerResponse> SaveInviteAsync(
        InviteRemoteServerRequest request,
        CancellationToken cancellationToken = default)
    {
        string currentStep = "validate invite";

        try
        {
            ArgumentNullException.ThrowIfNull(request);

            currentStep = "validate wg0.conf";
            string content = ValidateWg0Config(request.Wg0Config);

            currentStep = "validate control API key";
            string controlApiKey = RequireSingleLine(request.RemoteApiKey, "Remote API key");

            currentStep = "create VPN directory";
            Directory.CreateDirectory(InstallStateConstants.VpnRootPath);

            currentStep = "save wg0.conf";
            await File.WriteAllTextAsync(InstallStateConstants.WireGuardConfigFilePath, content, cancellationToken);

            currentStep = "set wg0.conf permissions";
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(
                    InstallStateConstants.WireGuardConfigFilePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            currentStep = "save control API key";
            await _authService.SaveControlApiKeyAsync(controlApiKey, cancellationToken);

            currentStep = "update cluster ID";
            string clusterId = await _serverConfigService.UpdateClusterIdAsync(request.ClusterId, cancellationToken);

            currentStep = "enable and restart WireGuard";
            string wireGuardMessage = await _installStateService.EnableAndRestartWireGuardAsync(cancellationToken);

            currentStep = "restart ASA if running";
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
            _logger.LogWarning(ex, "VPN invite failed during step: {Step}", currentStep);
            return new InviteRemoteServerResponse(false, $"VPN invite failed during {currentStep}. {ex.Message}", null);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "VPN invite failed during step: {Step}", currentStep);
            return new InviteRemoteServerResponse(false, $"VPN invite failed during {currentStep}. {ex.Message}", null);
        }
    }

    public async Task<InviteRemoteServerResponse> ClaimInviteAsync(
        string? inviteUrl,
        CancellationToken cancellationToken = default)
    {
        Uri? inviteUri = null;

        try
        {
            if (string.IsNullOrWhiteSpace(inviteUrl))
            {
                throw new ArgumentException("Invite URL is required.");
            }

            if (!Uri.TryCreate(inviteUrl.Trim(), UriKind.Absolute, out inviteUri) ||
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
            _logger.LogWarning(ex, "VPN invite fetch failed for {InviteUrl}", inviteUrl);
            return new InviteRemoteServerResponse(false, ex.Message, null);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "VPN invite fetch failed for {InviteUrl}", inviteUri?.ToString() ?? inviteUrl);
            return new InviteRemoteServerResponse(false, ex.Message, null);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "VPN invite fetch HTTP failure for {InviteUrl}", inviteUri?.ToString() ?? inviteUrl);
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
