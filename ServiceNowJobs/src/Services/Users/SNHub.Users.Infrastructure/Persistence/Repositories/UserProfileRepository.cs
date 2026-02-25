using Microsoft.EntityFrameworkCore;
using SNHub.Users.Application.Interfaces;
using SNHub.Users.Domain.Entities;

namespace SNHub.Users.Infrastructure.Persistence.Repositories;

public sealed class UserProfileRepository : IUserProfileRepository
{
    private readonly UsersDbContext _db;
    public UserProfileRepository(UsersDbContext db) => _db = db;

    public Task<UserProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);

    public Task<UserProfile?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.UserProfiles.FindAsync([id], ct).AsTask();

    public async Task<(IEnumerable<UserProfile> Items, int Total)> GetPagedAsync(
        string? search, bool? isDeleted, int page, int pageSize, CancellationToken ct = default)
    {
        var q = _db.UserProfiles.AsNoTracking().AsQueryable();

        if (isDeleted.HasValue)
            q = q.Where(p => p.IsDeleted == isDeleted.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(p =>
                (p.FirstName != null && p.FirstName.ToLower().Contains(s)) ||
                (p.LastName  != null && p.LastName.ToLower().Contains(s))  ||
                (p.Email     != null && p.Email.ToLower().Contains(s)));
        }

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task AddAsync(UserProfile profile, CancellationToken ct = default)
        => await _db.UserProfiles.AddAsync(profile, ct);

    public Task UpdateAsync(UserProfile profile, CancellationToken ct = default)
    {
        _db.UserProfiles.Update(profile);
        return Task.CompletedTask;
    }
}
