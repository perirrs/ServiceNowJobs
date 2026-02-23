using Dapper;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Domain.Entities;
using SNHub.Auth.Domain.Enums;
using SNHub.Auth.Infrastructure.Persistence;

namespace SNHub.Auth.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly AuthDbContext _context;
    private readonly string _connectionString;

    public UserRepository(AuthDbContext context, IConfiguration configuration)
    {
        _context = context;
        _connectionString = configuration.GetConnectionString("AuthDb")
            ?? throw new InvalidOperationException("AuthDb connection string not configured.");
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
        => await _context.Users.AsNoTracking()
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

    public Task UpdateAsync(User user, CancellationToken ct = default)
    {
        _context.Users.Update(user);
        return Task.CompletedTask;
    }

    /// <summary>Dapper-powered paged query — efficient for admin user listing.</summary>
    public async Task<(IEnumerable<User> Users, int TotalCount)> GetPagedAsync(
        int page, int pageSize,
        UserRole? roleFilter = null,
        bool? isActiveFilter = null,
        string? searchTerm = null,
        CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);

        var conditions = new List<string>();
        var p = new DynamicParameters();
        p.Add("Offset", (page - 1) * pageSize);
        p.Add("PageSize", pageSize);

        if (roleFilter.HasValue)
        {
            conditions.Add("@Role = ANY(roles)");
            p.Add("Role", (int)roleFilter.Value);
        }

        if (isActiveFilter.HasValue)
        {
            conditions.Add("is_active = @IsActive");
            p.Add("IsActive", isActiveFilter.Value);
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            conditions.Add("(email ILIKE @Search OR first_name ILIKE @Search OR last_name ILIKE @Search)");
            p.Add("Search", $"%{searchTerm}%");
        }

        var where = conditions.Count != 0 ? $"WHERE {string.Join(" AND ", conditions)}" : string.Empty;

        var count = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM auth.users {where}", p);
        var users = await conn.QueryAsync<User>(
            $@"SELECT id, email, normalized_email, first_name, last_name,
                      is_active, is_email_verified, roles, created_at, updated_at
               FROM auth.users {where}
               ORDER BY created_at DESC
               OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", p);

        return (users, count);
    }
}
