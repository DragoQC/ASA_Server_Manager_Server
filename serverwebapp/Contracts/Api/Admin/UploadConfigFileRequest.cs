using Microsoft.AspNetCore.Http;

namespace AsaServerManager.Web.Contracts.Api.Admin;

public sealed class UploadConfigFileRequest
{
    public IFormFile? File { get; set; }
}
