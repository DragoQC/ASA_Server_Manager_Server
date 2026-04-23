using asa_server_node_api.Data;
using asa_server_node_api.Data.Entities;
using asa_server_node_api.Models.Actions;
using Microsoft.EntityFrameworkCore;

namespace asa_server_node_api.Services;

public sealed class ActionMappingService(IDbContextFactory<AppDbContext> dbContextFactory)
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory = dbContextFactory;

    public async Task<ActionMapping?> ResolveAsync(string commandText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return null;
        }

        string normalized = commandText.Trim().ToUpperInvariant();

        await using AppDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        ActionMappingEntity? entity = await dbContext.ActionMappings
            .AsNoTracking()
            .SingleOrDefaultAsync(mapping => mapping.NormalizedCommandText == normalized, cancellationToken);

        return entity is null
            ? null
            : new ActionMapping(entity.CommandText, entity.ActionType, entity.ActionValue, entity.Description);
    }
}
