namespace asa_server_node_api.Models;

public sealed record AsaServerStatusSnapshot(
		string ServiceName,
		string LoadState,
		string ActiveState,
		string SubState,
		string UnitFileState,
		int? MainPid,
		string Result,
		DateTimeOffset? ActiveSinceUtc,
		DateTimeOffset CheckedAtUtc,
		string? ErrorMessage)
{
	public bool IsAvailable => string.IsNullOrWhiteSpace(ErrorMessage);

	public bool IsInstalled => !string.Equals(LoadState, "not-found", StringComparison.OrdinalIgnoreCase);

	public bool IsRunning =>
			string.Equals(ActiveState, "active", StringComparison.OrdinalIgnoreCase) &&
			string.Equals(SubState, "running", StringComparison.OrdinalIgnoreCase);

	public bool CanManage => IsRunning;

	public string StatusLabel => (ActiveState, SubState, ErrorMessage, IsInstalled) switch
	{
		(_, _, not null, _) => "Unavailable",
		(_, _, _, false) => "Not installed",
		("active", "running", _, _) => "Running",
		("activating", _, _, _) => "Starting",
		("deactivating", _, _, _) => "Stopping",
		("failed", _, _, _) => "Failed",
		_ => "Stopped"
	};

	public string StatusDescription => StatusLabel switch
	{
		"Running" => "ASA service is active and ready for management.",
		"Starting" => "ASA service is starting up.",
		"Stopping" => "ASA service is shutting down.",
		"Failed" => "ASA service exists but the last run failed.",
		"Not installed" => "The asa systemd service was not found on this host.",
		"Unavailable" => ErrorMessage ?? "Server state is currently unavailable.",
		_ => "ASA service is not running."
	};

	public static AsaServerStatusSnapshot Default(string serviceName) =>
			new(
					serviceName,
					LoadState: "unknown",
					ActiveState: "unknown",
					SubState: "unknown",
					UnitFileState: "unknown",
					MainPid: null,
					Result: "unknown",
					ActiveSinceUtc: null,
					CheckedAtUtc: DateTimeOffset.UtcNow,
					ErrorMessage: "Waiting for first server check.");
}
