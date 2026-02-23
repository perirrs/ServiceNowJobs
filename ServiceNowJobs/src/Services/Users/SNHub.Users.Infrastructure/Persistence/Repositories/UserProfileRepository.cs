using Microsoft.EntityFrameworkCore;
using SNHub.Users.Application.Interfaces;
using SNHub.Users.Domain.Entities;

namespace SNHub.Users.Infrastructure.Persistence.Repositories;

public sealed class UserProfileRepository : IUserProfileRepository
{
    private readonly UsersDbContext _db;
    public UserProfileRepository(UsersDbContext db) { _db = db; }

    public Task<UserProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);

    public Task<UserProfile?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.UserProfiles.FindAsync([id], ct).AsTask();

    public async Task AddAsync(UserProfile profile, CancellationToken ct = default)
        => await _db.UserProfiles.AddAsync(profile, ct);

    public Task UpdateAsync(UserProfile profile, CancellationToken ct = default)
    {
        _db.UserProfiles.Update(profile);
        return Task.CompletedTask;
    }
}
