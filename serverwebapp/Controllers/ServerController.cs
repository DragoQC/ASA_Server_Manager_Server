using AsaServerManager.Web.Contracts.Api.Admin;
using AsaServerManager.Web.Infrastructure.Auth;
using AsaServerManager.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AsaServerManager.Web.Controllers;

[ApiController]
[RequireApiKey]
[Route("api/admin/server")]
public sealed class ServerController(UploadedFileService uploadedFileService) : ControllerBase
{
    private readonly UploadedFileService _uploadedFileService = uploadedFileService;

    [HttpPost("env")]
    public async Task<IActionResult> UploadEnv([FromForm] UploadConfigFileRequest request, CancellationToken cancellationToken)
    {
        UploadFileResult result = await _uploadedFileService.UploadServerConfigAsync(request.File, cancellationToken);

        return result.Success ? Ok(result) : BadRequest(result);
    }
}
