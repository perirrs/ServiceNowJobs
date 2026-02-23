using Microsoft.EntityFrameworkCore;
using SNHub.Profiles.Application.Interfaces;
using SNHub.Profiles.Domain.Entities;
namespace SNHub.Profiles.Infrastructure.Persistence.Repositories;
public sealed class CandidateProfileRepository : ICandidateProfileRepository
{
    private readonly ProfilesDbContext _db;
    public CandidateProfileRepository(ProfilesDbContext db) => _db = db;
    public Task<CandidateProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => _db.CandidateProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
    public async Task AddAsync(CandidateProfile profile, CancellationToken ct = default)
        => await _db.CandidateProfiles.AddAsync(profile, ct);
    public async Task<(IEnumerable<CandidateProfile> Items, int Total)> SearchAsync(string? keyword, string? country, int? minExperience, bool? openToRemote, int page, int pageSize, CancellationToken ct = default)
    {
        var q = _db.CandidateProfiles.Where(p => p.IsPublic);
        if (!string.IsNullOrWhiteSpace(keyword)) q = q.Where(p => EF.Functions.ILike(p.Headline ?? "", $"%{keyword}%") || EF.Functions.ILike(p.Bio ?? "", $"%{keyword}%"));
        if (!string.IsNullOrWhiteSpace(country)) q = q.Where(p => p.Country == country);
        if (minExperience.HasValue) q = q.Where(p => p.YearsOfExperience >= minExperience);
        if (openToRemote.HasValue) q = q.Where(p => p.OpenToRemote == openToRemote);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(p => p.ProfileCompleteness).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }
}
public sealed class EmployerProfileRepository : IEmployerProfileRepository
{
    private readonly ProfilesDbContext _db;
    public EmployerProfileRepository(ProfilesDbContext db) => _db = db;
    public Task<EmployerProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => _db.EmployerProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
    public async Task AddAsync(EmployerProfile profile, CancellationToken ct = default)
        => await _db.EmployerProfiles.AddAsync(profile, ct);
}
