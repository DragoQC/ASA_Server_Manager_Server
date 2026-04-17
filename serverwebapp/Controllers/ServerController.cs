using AsaServerManager.Web.Contracts.Api.Admin;
using AsaServerManager.Web.Infrastructure.Auth;
using AsaServerManager.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AsaServerManager.Web.Controllers;

[ApiController]
[RequireApiKey]
[Route("api/admin/server")]
public sealed class ServerController(ServerConfigService serverConfigService) : ControllerBase
{
    private readonly ServerConfigService _serverConfigService = serverConfigService;

    [HttpPost("env")]
    public async Task<IActionResult> UploadEnv([FromForm] UploadConfigFileRequest request, CancellationToken cancellationToken)
    {
        try
        {
            string content = await ReadFileContentAsync(request.File, cancellationToken);
            await _serverConfigService.SaveRawAsync(content, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }

        return Ok(new
        {
            success = true,
            message = "asa.env updated.",
            path = Constants.ServerConfigConstants.EnvFilePath
        });
    }

    private static async Task<string> ReadFileContentAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length <= 0)
        {
            throw new ArgumentException("A config file upload is required.");
        }

        using Stream stream = file.OpenReadStream();
        using StreamReader reader = new(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
