using System.Security.Claims;
using System.Security.Cryptography;
using AsaServerManager.Web.Data;
using AsaServerManager.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace AsaServerManager.Web.Services;

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

	public Task<bool> IsApiKeyValidAsync(string? apiKey, CancellationToken cancellationToken = default)
	{
		return IsRemoteApiKeyValidAsync(apiKey, cancellationToken);
	}

	public async Task<string?> LoadRemoteApiKeyAsync(CancellationToken cancellationToken = default)
	{
		await using AppDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
		RemoteKeyEntity? entity = await dbContext.RemoteKeys
				.AsNoTracking()
				.OrderBy(remoteKey => remoteKey.Id)
				.FirstOrDefaultAsync(cancellationToken);

		return entity?.ApiKey;
	}

	public async Task SaveRemoteApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

		await using AppDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
		RemoteKeyEntity? entity = await dbContext.RemoteKeys.FirstOrDefaultAsync(cancellationToken);

		if (entity is null)
		{
			entity = new RemoteKeyEntity
			{
				Id = 1,
				ApiKey = apiKey
			};

			dbContext.RemoteKeys.Add(entity);
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

	public async Task<bool> IsRemoteApiKeyValidAsync(string? apiKey, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(apiKey))
		{
			return false;
		}

		string? configuredKey = await LoadRemoteApiKeyAsync(cancellationToken);
		if (string.IsNullOrWhiteSpace(configuredKey))
		{
			return false;
		}

		return string.Equals(configuredKey, apiKey, StringComparison.Ordinal);
	}
}
