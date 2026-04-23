namespace asa_server_node_api.Models;

public sealed record BackupArchiveInfo(
    string Format,
    string FileName,
    string FilePath,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc)
{
    public string SizeText => FormatBytes(SizeBytes);

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }
}
