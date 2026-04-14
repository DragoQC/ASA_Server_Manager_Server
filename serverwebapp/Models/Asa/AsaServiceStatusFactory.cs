using AsaServerManager.Web.Models;

namespace AsaServerManager.Web.Models.Asa;

public static class AsaServiceStatusFactory
{
    public static AsaServiceStatus Create(
        string activeState,
        string subState,
        string result,
        string unitFileState,
        DateTimeOffset? activeSinceUtc)
    {
        string displayText = activeState switch
        {
            "active" => "Running",
            "activating" => "Starting",
            "deactivating" => "Stopping",
            "inactive" => "Stopped",
            "failed" => "Failed",
            _ => $"{UppercaseFirst(activeState)} ({subState})"
        };

        bool canStart = activeState == "inactive" || activeState == "failed";
        bool canStop = activeState == "active" || activeState == "activating";
        string uptimeText = GetUptimeText(activeState, activeSinceUtc);

        return new AsaServiceStatus(
            activeState,
            subState,
            result,
            unitFileState,
            displayText,
            canStart,
            canStop,
            activeSinceUtc,
            uptimeText);
    }

    public static AsaServiceStatus FromSnapshot(AsaServerStatusSnapshot snapshot)
    {
        return Create(
            snapshot.ActiveState,
            snapshot.SubState,
            snapshot.Result,
            snapshot.UnitFileState,
            snapshot.ActiveSinceUtc);
    }

    public static DateTimeOffset? TryParseSystemdTimestamp(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "n/a")
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    private static string GetUptimeText(string activeState, DateTimeOffset? activeSinceUtc)
    {
        if (!string.Equals(activeState, "active", StringComparison.Ordinal) || activeSinceUtc is null)
        {
            return "Unavailable";
        }

        TimeSpan uptime = DateTimeOffset.UtcNow - activeSinceUtc.Value;
        if (uptime < TimeSpan.Zero)
        {
            return "Unavailable";
        }

        if (uptime.TotalDays >= 1)
        {
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h";
        }

        if (uptime.TotalHours >= 1)
        {
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        }

        if (uptime.TotalMinutes >= 1)
        {
            return $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";
        }

        return $"{Math.Max(0, uptime.Seconds)}s";
    }

    private static string UppercaseFirst(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "Unknown"
            : char.ToUpperInvariant(value[0]) + value[1..];
    }
}
