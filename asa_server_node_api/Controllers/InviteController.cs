using asa_server_node_api.Contracts.Api.Admin;
using asa_server_node_api.Infrastructure.Auth;
using asa_server_node_api.Services;
using Microsoft.AspNetCore.Mvc;

namespace asa_server_node_api.Controllers;

[ApiController]
[RequireApiKey]
[Route("api/admin/invite")]
public sealed class InviteController(VpnConfigService vpnConfigService) : ControllerBase
{
    private readonly VpnConfigService _vpnConfigService = vpnConfigService;

    [HttpPost]
    public async Task<IActionResult> Invite([FromBody] InviteRemoteServerRequest request, CancellationToken cancellationToken)
    {
        InviteRemoteServerResponse result = await _vpnConfigService.SaveInviteAsync(request, cancellationToken);
        return result.Accepted ? Ok(result) : BadRequest(result);
    }
}
