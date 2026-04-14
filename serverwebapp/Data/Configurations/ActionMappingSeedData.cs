using AsaServerManager.Web.Data.Entities;

namespace AsaServerManager.Web.Data.Configurations;

public static class ActionMappingSeedData
{
    public static readonly DateTimeOffset SeedTimestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static ActionMappingEntity[] Create() =>
    [
        new()
        {
            Id = 1,
            CreatedAtUtc = SeedTimestamp,
            ModifiedAtUtc = SeedTimestamp,
            CommandText = "clear",
            NormalizedCommandText = "CLEAR",
            ActionType = "clear-window",
            ActionValue = string.Empty,
            Description = "Clears the shell transcript."
        },
        new()
        {
            Id = 2,
            CreatedAtUtc = SeedTimestamp,
            ModifiedAtUtc = SeedTimestamp,
            CommandText = "cls",
            NormalizedCommandText = "CLS",
            ActionType = "clear-window",
            ActionValue = string.Empty,
            Description = "Clears the shell transcript."
        }
    ];
}
