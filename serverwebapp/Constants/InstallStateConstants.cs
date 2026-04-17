namespace AsaServerManager.Web.Constants;

public static class InstallStateConstants
{
	public const string BaseDirectoryPath = "/opt/asa";
	public const string ProtonRootPath = BaseDirectoryPath + "/proton";
	public const string SteamRootPath = BaseDirectoryPath + "/steam";
	public const string SteamCmdPath = SteamRootPath + "/steamcmd.sh";
	public const string SteamCmdArchivePath = SteamRootPath + "/steamcmd_linux.tar.gz";
	public const string SteamCmdDownloadUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz";
	public const string ServerRootPath = BaseDirectoryPath + "/server";
	public const string StartScriptPath = BaseDirectoryPath + "/start-asa.sh";
	public const string ServiceFilePath = BaseDirectoryPath + "/systemd/asa.service";
}
