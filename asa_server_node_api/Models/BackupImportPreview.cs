namespace asa_server_node_api.Models;

public sealed record BackupImportPreview(
    string Format,
    string FileName,
    string ArchivePath,
    long SizeBytes,
    int EntryCount,
    IReadOnlyList<string> PreviewEntries,
    string RestorePath)
{
    public string SizeText => BackupArchiveInfo.FormatBytes(SizeBytes);
}
