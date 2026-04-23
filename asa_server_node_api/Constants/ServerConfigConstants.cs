namespace asa_server_node_api.Constants;

public static class ServerConfigConstants
{
    public const string EnvFilePath = "/opt/asa/server/asa.env";
    public const string MapNameEnvKey = "MAP_NAME";
    public const string ServerNameEnvKey = "SERVER_NAME";
    public const string MaxPlayersEnvKey = "MAX_PLAYERS";
    public const string GamePortEnvKey = "GAME_PORT";
    public const string QueryPortEnvKey = "QUERY_PORT";
    public const string RconPortEnvKey = "RCON_PORT";
    public const string ModIdsEnvKey = "MOD_IDS";
    public const string ClusterIdEnvKey = "CLUSTER_ID";
    public const string ClusterDirEnvKey = "CLUSTER_DIR";
    public const string ExtraArgsEnvKey = "EXTRA_ARGS";

    public static IReadOnlySet<string> EnvKeys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        MapNameEnvKey,
        ServerNameEnvKey,
        MaxPlayersEnvKey,
        GamePortEnvKey,
        QueryPortEnvKey,
        RconPortEnvKey,
        ModIdsEnvKey,
        ClusterIdEnvKey,
        ClusterDirEnvKey,
        ExtraArgsEnvKey
    };
}
