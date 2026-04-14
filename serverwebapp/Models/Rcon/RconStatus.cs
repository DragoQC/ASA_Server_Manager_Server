namespace AsaServerManager.Web.Models.Rcon;

public sealed record RconStatus(
		bool HasGameUserSettingsFile,
		bool HasEnabledKey,
		bool IsEnabled,
		bool HasPasswordKey,
		bool HasPassword,
		int Port,
		string StateLabel,
		string Message)
{
	public bool CanExecute => HasGameUserSettingsFile && IsEnabled && HasPassword;

	public static RconStatus Unknown(int port = 27020) =>
			new(
					false,
					false,
					false,
					false,
					false,
					port,
					"Missing",
					"Checking RCON...");
}
