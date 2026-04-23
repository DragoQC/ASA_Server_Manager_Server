using System.Security.Claims;
using System.Security.Cryptography;
using asa_server_node_api.Constants;
using asa_server_node_api.Data;
using asa_server_node_api.Data.Entities;
using asa_server_node_api.Models.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace asa_server_node_api.Services;

public sealed class AuthService(UserManager<ApplicationUser> userManager, IDbContextFactory<AppDbContext> dbContextFactory)
{
	private readonly UserManager<ApplicationUser> _userManager = userManager;
	private readonly IDbContextFactory<AppDbContext> _dbContextFactory = dbContextFactory;

	public async Task EnsureDefaultAdminUserAsync(CancellationToken cancellationToken = default)
	{
		ApplicationUser? user = await _userManager.FindByNameAsync("admin");
		if (user is not null)
		{
			return;
		}

		ApplicationUser adminUser = new()
		{
			UserName = "admin",
			Email = "admin@local",
			EmailConfirmed = true
		};

		IdentityResult result = await _userManager.CreateAsync(adminUser, "admin");
		if (!result.Succeeded)
		{
			string errors = string.Join(' ', result.Errors.Select(error => error.Description));
			throw new InvalidOperationException($"Default admin user creation failed. {errors}");
		}
	}

	public async Task<bool> MustChangePasswordAsync(ClaimsPrincipal principal)
	{
		ApplicationUser? user = await _userManager.GetUserAsync(principal);
		if (user is null)
		{
			return false;
		}

		return await MustChangePasswordAsync(user);
	}

	public async Task<bool> MustChangePasswordAsync(string? username)
	{
		if (string.IsNullOrWhiteSpace(username))
		{
			return false;
		}

		ApplicationUser? user = await _userManager.FindByNameAsync(username);
		if (user is null)
		{
			return false;
		}

		return await MustChangePasswordAsync(user);
	}

	public async Task<IdentityResult> ChangePasswordAsync(ClaimsPrincipal principal, string newPassword)
	{
		ApplicationUser? user = await _userManager.GetUserAsync(principal);
		if (user is null)
		{
			return IdentityResult.Failed(new IdentityError
			{
				Description = "Current user was not found."
			});
		}

		string passwordHash = _userManager.PasswordHasher.HashPassword(user, newPassword);
		user.PasswordHash = passwordHash;
		user.SecurityStamp = Guid.NewGuid().ToString("N");

		return await _userManager.UpdateAsync(user);
	}

	public async Task<bool> IsApiKeyValidAsync(string? apiKey, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(apiKey))
		{
			return false;
		}

		await using AppDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
		return await dbContext.ApiKeys
			.AsNoTracking()
			.AnyAsync(entity => entity.ApiKey == apiKey, cancellationToken);
	}

	public Task<string?> LoadAppApiKeyAsync(CancellationToken cancellationToken = default)
	{
		return LoadApiKeyAsync(ApiKeyTypeConstants.App, cancellationToken);
	}

	public Task<string?> LoadControlApiKeyAsync(CancellationToken cancellationToken = default)
	{
		return LoadApiKeyAsync(ApiKeyTypeConstants.Control, cancellationToken);
	}

	public async Task<IReadOnlyList<StoredApiKey>> LoadApiKeysAsync(CancellationToken cancellationToken = default)
	{
		await using AppDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
		return await dbContext.ApiKeys
			.AsNoTracking()
			.OrderBy(entity => entity.Type)
			.Select(entity => new StoredApiKey(
				entity.Type,
				entity.ApiKey,
				entity.ModifiedAtUtc))
			.ToListAsync(cancellationToken);
	}

	public Task SaveAppApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
	{
		return SaveApiKeyAsync(ApiKeyTypeConstants.App, apiKey, cancellationToken);
	}

	public Task SaveControlApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
	{
		return SaveApiKeyAsync(ApiKeyTypeConstants.Control, apiKey, cancellationToken);
	}

	public async Task DeleteApiKeyAsync(string type, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(type);

		await using AppDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
		ApiKeyEntity? entity = await dbContext.ApiKeys
			.FirstOrDefaultAsync(item => item.Type == type, cancellationToken);

		if (entity is null)
		{
			return;
		}

		dbContext.ApiKeys.Remove(entity);
		await dbContext.SaveChangesAsync(cancellationToken);
	}

	private async Task<string?> LoadApiKeyAsync(string type, CancellationToken cancellationToken = default)
	{
		await using AppDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
		ApiKeyEntity? entity = await dbContext.ApiKeys
			.AsNoTracking()
			.FirstOrDefaultAsync(item => item.Type == type, cancellationToken);

		return entity?.ApiKey;
	}

	private async Task SaveApiKeyAsync(string type, string apiKey, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(type);
		ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

		await using AppDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
		ApiKeyEntity? entity = await dbContext.ApiKeys
			.FirstOrDefaultAsync(item => item.Type == type, cancellationToken);

		if (entity is null)
		{
			entity = new ApiKeyEntity
			{
				Type = type,
				ApiKey = apiKey
			};

			dbContext.ApiKeys.Add(entity);
		}
		else
		{
			entity.ApiKey = apiKey;
		}

		await dbContext.SaveChangesAsync(cancellationToken);
	}

	public static string GenerateRemoteApiKey()
	{
		byte[] bytes = RandomNumberGenerator.GetBytes(32);
		return Convert.ToHexString(bytes);
	}

	private async Task<bool> MustChangePasswordAsync(ApplicationUser user)
	{
		bool isDefaultAdmin = string.Equals(user.UserName, "admin", StringComparison.OrdinalIgnoreCase);
		if (!isDefaultAdmin)
		{
			return false;
		}

		return await _userManager.CheckPasswordAsync(user, "admin");
	}

}
