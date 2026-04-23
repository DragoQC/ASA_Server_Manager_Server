using Microsoft.AspNetCore.Http;

namespace asa_server_node_api.Contracts.Api.Admin;

public sealed class UploadConfigFileRequest
{
    public IFormFile? File { get; set; }
}
