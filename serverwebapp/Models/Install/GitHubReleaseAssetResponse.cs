using System.Text.Json.Serialization;

namespace AsaServerManager.Web.Models.Install;

internal sealed record GitHubReleaseAssetResponse(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
