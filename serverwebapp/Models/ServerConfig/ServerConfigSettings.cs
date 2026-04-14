using System.ComponentModel.DataAnnotations;

namespace AsaServerManager.Web.Models.ServerConfig;

public sealed class ServerConfigSettings
{
	[Required]
	[StringLength(128)]
	public string MapName { get; set; } = "TheIsland_WP";

	[Required]
	[StringLength(128)]
	public string ServerName { get; set; } = "ARK ASA Server";

	[Range(1, 200)]
	public int MaxPlayers { get; set; } = 20;

	[Range(1, 65535)]
	public int GamePort { get; set; } = 7777;

	[Range(1, 65535)]
	public int QueryPort { get; set; } = 27015;

	[Range(1, 65535)]
	public int RconPort { get; set; } = 27020;

	public string ModIds { get; set; } = string.Empty;

	[StringLength(128)]
	public string ClusterId { get; set; } = string.Empty;

	[Required]
	[StringLength(256)]
	public string ClusterDir { get; set; } = "/opt/asa/cluster";

	public string CustomExtraArgs { get; set; } = "-crossplay -NoBattlEye";

	public static ServerConfigSettings Default() => new();
}
