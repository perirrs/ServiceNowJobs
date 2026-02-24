using Microsoft.EntityFrameworkCore;
using SNHub.Auth.Application.DTOs;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Domain.Entities;
using SNHub.Auth.Domain.Enums;
using SNHub.Auth.Infrastructure.Persistence;

namespace SNHub.Auth.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly AuthDbContext _context;
    public UserRepository(AuthDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, ct);

    // NOTE: No AsNoTracking — callers (ForgotPassword, ResetPassword, VerifyEmail)
    // mutate this entity and call SaveChangesAsync. AsNoTracking would cause
    // DbUpdateConcurrencyException because EF Core can't UPDATE an untracked entity.
    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
        => await _context.Users
            .FirstOrDefaultAsync(u => u.NormalizedEmail == email.ToUpperInvariant(), ct);

    public async Task<User?> GetByEmailWithTokensAsync(string email, CancellationToken ct = default)
        => await _context.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == email.ToUpperInvariant(), ct);

    public async Task<User?> GetByIdWithTokensAsync(Guid id, CancellationToken ct = default)
        => await _context.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<User?> GetByRefreshTokenAsync(string token, CancellationToken ct = default)
        => await _context.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.RefreshTokens.Any(rt => rt.Token == token), ct);

    public async Task<User?> GetByLinkedInIdAsync(string linkedInId, CancellationToken ct = default)
        => await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.LinkedInId == linkedInId, ct);

    public async Task<User?> GetByAzureAdObjectIdAsync(string objectId, CancellationToken ct = default)
        => await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.AzureAdObjectId == objectId, ct);

    public async Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default)
        => await _context.Users
            .AnyAsync(u => u.NormalizedEmail == email.ToUpperInvariant(), ct);

    public async Task AddAsync(User user, CancellationToken ct = default)
        => await _context.Users.AddAsync(user, ct);

    /// <summary>
    /// Explicitly adds a RefreshToken to the DbSet change tracker.
    /// This is required when adding a token to an already-tracked User because EF Core
    /// cannot detect additions to a plain List&lt;T&gt; backing field — only its own
    /// ObservableHashSet/ObservableCollection types raise collection-change events.
    /// Calling AddAsync here marks the entity as EntityState.Added so SaveChangesAsync
    /// emits an INSERT rather than silently skipping it.
    /// </summary>
    public async Task AddRefreshTokenAsync(RefreshToken token, CancellationToken ct = default)
        => await _context.RefreshTokens.AddAsync(token, ct);

    public Task UpdateAsync(User user, CancellationToken ct = default)
    {
        _context.Users.Update(user);
        return Task.CompletedTask;
    }

    /// <summary>
    /// EF Core projection — avoids Dapper mapping to User (private setters issue).
    /// Projects directly to UserSummaryDto for clean, efficient paged listing.
    /// </summary>
    public async Task<(IReadOnlyList<UserSummaryDto> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize,
        UserRole? roleFilter = null,
        bool? isActiveFilter = null,
        string? searchTerm = null,
        CancellationToken ct = default)
    {
        var query = _context.Users.AsNoTracking();

        if (roleFilter.HasValue)
        {
            // RolesJson stores compact JSON arrays e.g. "[1,5,7]" (no spaces).
            // Role values are 1-9 so no false-positive digit collisions.
            // Handles: single "[5]", first "[5,", last ",5]", middle ",5,"
            var r = (int)roleFilter.Value;
            query = query.Where(u =>
                u.RolesJson == $"[{r}]" ||
                u.RolesJson.StartsWith($"[{r},") ||
                u.RolesJson.EndsWith($",{r}]") ||
                u.RolesJson.Contains($",{r},"));
        }

        if (isActiveFilter.HasValue)
            query = query.Where(u => u.IsActive == isActiveFilter.Value);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var upper = searchTerm.ToUpperInvariant();
            var lower = searchTerm.ToLower();
            query = query.Where(u =>
                u.NormalizedEmail.Contains(upper) ||
                (u.FirstName + " " + u.LastName).ToLower().Contains(lower));
        }

        var total = await query.CountAsync(ct);

        // u.Roles is Ignored by EF — cannot project in SQL.
        // Project to anonymous type (SQL-safe), then parse RolesJson in memory.
        var rawItems = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                u.Id, u.Email, u.FirstName, u.LastName,
                u.IsActive, u.IsEmailVerified, u.IsSuspended,
                u.RolesJson, u.CreatedAt
            })
            .ToListAsync(ct);

        var items = rawItems.Select(u => new UserSummaryDto(
            u.Id, u.Email, u.FirstName, u.LastName,
            $"{u.FirstName} {u.LastName}".Trim(),
            u.IsActive, u.IsEmailVerified, u.IsSuspended,
            System.Text.Json.JsonSerializer.Deserialize<List<int>>(u.RolesJson)!
                .Select(i => ((UserRole)i).ToString()),
            u.CreatedAt)).ToList();

        return (items, total);
    }
}