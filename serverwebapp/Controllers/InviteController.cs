using AsaServerManager.Web.Contracts.Api.Admin;
using AsaServerManager.Web.Infrastructure.Auth;
using AsaServerManager.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AsaServerManager.Web.Controllers;

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
