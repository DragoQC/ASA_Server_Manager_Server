using AsaServerManager.Web.Models.Asa;

namespace AsaServerManager.Web.Models.AsaLogs;

public sealed record AsaLogsSnapshot(
    AsaServiceStatus ServiceStatus,
    LogSectionSnapshot StatusSection,
    LogSectionSnapshot WebAppJournalSection,
    LogSectionSnapshot WireGuardJournalSection,
    LogSectionSnapshot NfsJournalSection,
    DateTimeOffset UpdatedAtUtc);
