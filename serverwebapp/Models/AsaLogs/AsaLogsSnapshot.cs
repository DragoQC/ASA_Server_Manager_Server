using AsaServerManager.Web.Models.Asa;

namespace AsaServerManager.Web.Models.AsaLogs;

public sealed record AsaLogsSnapshot(
    AsaServiceStatus ServiceStatus,
    LogSectionSnapshot StatusSection,
    LogSectionSnapshot WireGuardJournalSection,
    LogSectionSnapshot NfsJournalSection,
    DateTimeOffset UpdatedAtUtc);
