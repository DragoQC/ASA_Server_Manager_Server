using System.Text.Json.Serialization;

namespace AsaServerManager.Web.Models.Install;

internal sealed record GitHubReleaseResponse(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("assets")] IReadOnlyList<GitHubReleaseAssetResponse>? Assets);
