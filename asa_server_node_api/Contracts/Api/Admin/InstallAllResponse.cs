using asa_server_node_api.Models.ServerConfig;

namespace asa_server_node_api.Contracts.Api.Admin;

public sealed record InstallAllResponse(
    string ProtonMessage,
    string SteamMessage,
    string StartScriptMessage,
    string ServiceFileMessage,
    ServerConfigSettings ServerConfig);
