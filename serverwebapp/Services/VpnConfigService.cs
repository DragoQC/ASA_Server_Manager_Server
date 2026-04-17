using AsaServerManager.Web.Constants;
using AsaServerManager.Web.Contracts.Api.Admin;

namespace AsaServerManager.Web.Services;

public sealed class VpnConfigService
{
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

            return new InviteRemoteServerResponse(
                true,
                "WireGuard client configuration saved.",
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

    private static string BuildConfigContent(InviteRemoteServerRequest request)
    {
        string vpnAddress = RequireSingleLine(request.VpnAddress, "VPN address");
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
