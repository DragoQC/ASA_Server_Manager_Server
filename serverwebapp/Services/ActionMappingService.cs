using AsaServerManager.Web.Data;
using AsaServerManager.Web.Data.Entities;
using AsaServerManager.Web.Models.Actions;
using Microsoft.EntityFrameworkCore;

namespace AsaServerManager.Web.Services;

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
