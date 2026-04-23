using asa_server_node_api.Models.Asa;

namespace asa_server_node_api.Models.AsaLogs;

public sealed record AsaLogsSnapshot(
    AsaServiceStatus ServiceStatus,
    LogSectionSnapshot StatusSection,
    LogSectionSnapshot WebAppJournalSection,
    LogSectionSnapshot WireGuardJournalSection,
    LogSectionSnapshot SmbJournalSection,
    DateTimeOffset UpdatedAtUtc);
