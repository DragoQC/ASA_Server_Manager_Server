namespace asa_server_node_api.Constants;

public static class InstallStateConstants
{
    public const string BaseDirectoryPath = "/opt/asa";
    public const string ClusterRootPath = BaseDirectoryPath + "/cluster";
    public const string SmbRootPath = BaseDirectoryPath + "/smb";
    public const string ProtonRootPath = BaseDirectoryPath + "/proton";
    public const string SteamRootPath = BaseDirectoryPath + "/steam";
	public const string SteamCmdPath = SteamRootPath + "/steamcmd.sh";
	public const string SteamCmdArchivePath = SteamRootPath + "/steamcmd_linux.tar.gz";
    public const string SteamCmdDownloadUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz";
    public const string BackupRootPath = BaseDirectoryPath + "/backup";
    public const string BackupImportRootPath = BackupRootPath + "/imports";
    public const string BackupRestoreWorkRootPath = BackupRootPath + "/restore-work";
    public const string ServerRootPath = BaseDirectoryPath + "/server";
    public const string VpnRootPath = BaseDirectoryPath + "/vpn";
    public const string PrepareClusterClientScriptPath = SmbRootPath + "/prepare-cluster-client.sh";
    public const string ApplySmbClientConfigScriptPath = SmbRootPath + "/apply-smb-client-config.sh";
    public const string PrepareZipToolsScriptPath = BackupRootPath + "/prepare-zip-tools.sh";
    public const string PrepareTarToolsScriptPath = BackupRootPath + "/prepare-tar-tools.sh";
    public const string SmbClientConfigFilePath = SmbRootPath + "/client.mount.cifs.conf";
    public const string WireGuardConfigFilePath = VpnRootPath + "/wg0.conf";
    public const string StartScriptPath = BaseDirectoryPath + "/start-asa.sh";
    public const string ServiceFilePath = BaseDirectoryPath + "/systemd/asa.service";
    public const string WebAppServiceName = "asa-webapp";
    public const string WireGuardInterfaceName = "wg0";
    public const string WireGuardServiceName = "wg-quick@" + WireGuardInterfaceName;
    public const string ClusterMountUnitName = "opt-asa-cluster.mount";
}
