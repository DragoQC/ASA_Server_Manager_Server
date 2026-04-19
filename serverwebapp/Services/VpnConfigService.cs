using System.Net.Http.Json;
using AsaServerManager.Web.Constants;
using AsaServerManager.Web.Contracts.Api.Admin;

namespace AsaServerManager.Web.Services;

public sealed class VpnConfigService
{
    private readonly ServerConfigService _serverConfigService;
    private readonly InstallStateService _installStateService;
    private readonly IHttpClientFactory _httpClientFactory;

    public VpnConfigService(
        ServerConfigService serverConfigService,
        InstallStateService installStateService,
        IHttpClientFactory httpClientFactory)
    {
        _serverConfigService = serverConfigService;
        _installStateService = installStateService;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<InviteRemoteServerResponse> SaveInviteAsync(
        InviteRemoteServerRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);

            string content = BuildConfigContent(request);

            Directory.CreateDirectory(InstallStateConstants.VpnRootPath);
            await File.WriteAllTextAsync(InstallStateConstants.WireGuardConfigFilePath, content, cancellationToken);
            string clusterId = await _serverConfigService.UpdateClusterIdAsync(request.ClusterId, cancellationToken);
            string restartMessage = await _installStateService.RestartAsaIfRunningAsync(cancellationToken);

            return new InviteRemoteServerResponse(
                true,
                string.IsNullOrWhiteSpace(clusterId)
                    ? $"WireGuard client configuration saved. Cluster ID cleared. {restartMessage}"
                    : $"WireGuard client configuration saved. Cluster ID set to {clusterId}. {restartMessage}",
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

    private static string BuildConfigContent(InviteRemoteServerRequest request)
    {
        string vpnAddress = RequireSingleLine(request.VpnAddress, "VPN address");
        RequireSingleLine(request.ClusterId, "Cluster ID");
        string serverEndpoint = RequireSingleLine(request.ServerEndpoint, "Server endpoint");
        string dns = RequireSingleLine(request.Dns, "DNS");
        string allowedIps = RequireSingleLine(request.AllowedIps, "Allowed IPs");
        string serverPublicKey = RequireSingleLine(request.ServerPublicKey, "Server public key");
        string clientPrivateKey = RequireSingleLine(request.ClientPrivateKey, "Client private key");
        string? presharedKey = string.IsNullOrWhiteSpace(request.PresharedKey)
            ? null
            : RequireSingleLine(request.PresharedKey, "Preshared key");

        List<string> lines =
        [
            "[Interface]",
            $"PrivateKey = {clientPrivateKey}",
            $"Address = {vpnAddress}",
            $"DNS = {dns}",
            string.Empty,
            "[Peer]",
            $"PublicKey = {serverPublicKey}"
        ];

        if (!string.IsNullOrWhiteSpace(presharedKey))
        {
            lines.Add($"PresharedKey = {presharedKey}");
        }

        lines.Add($"Endpoint = {serverEndpoint}");
        lines.Add($"AllowedIPs = {allowedIps}");
        lines.Add("PersistentKeepalive = 25");

        return string.Join('\n', lines) + "\n";
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
