namespace AsaServerManager.Web.Constants;

public static class InstallStateConstants
{
    public const string BaseDirectoryPath = "/opt/asa";
    public const string ProtonRootPath = "/opt/asa/proton";
    public const string SteamRootPath = "/opt/asa/steam";
    public const string SteamCmdPath = "/opt/asa/steam/steamcmd.sh";
    public const string SteamCmdArchivePath = "/opt/asa/steam/steamcmd_linux.tar.gz";
    public const string SteamCmdDownloadUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz";
    public const string ServerRootPath = "/opt/asa/server";
    public const string StartScriptPath = "/opt/asa/start-asa.sh";
    public const string ServiceFilePath = "/opt/asa/systemd/asa.service";
}
