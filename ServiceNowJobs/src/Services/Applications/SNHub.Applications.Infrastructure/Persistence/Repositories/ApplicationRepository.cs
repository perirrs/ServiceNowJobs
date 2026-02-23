using Microsoft.EntityFrameworkCore;
using SNHub.Applications.Application.Interfaces;
using SNHub.Applications.Domain.Entities;
using SNHub.Applications.Domain.Enums;

namespace SNHub.Applications.Infrastructure.Persistence.Repositories;

public sealed class ApplicationRepository : IApplicationRepository
{
    private readonly ApplicationsDbContext _db;
    public ApplicationRepository(ApplicationsDbContext db) => _db = db;

    public Task<JobApplication?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Applications.FindAsync([id], ct).AsTask();

    public Task<bool> ExistsAsync(Guid jobId, Guid candidateId, CancellationToken ct = default)
        => _db.Applications.AnyAsync(a => a.JobId == jobId && a.CandidateId == candidateId, ct);

    public async Task AddAsync(JobApplication app, CancellationToken ct = default)
        => await _db.Applications.AddAsync(app, ct);

    public async Task<(IEnumerable<JobApplication> Items, int Total)> GetByCandidateAsync(
        Guid candidateId, int page, int pageSize, CancellationToken ct = default)
    {
        var q = _db.Applications.Where(a => a.CandidateId == candidateId);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.AppliedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public async Task<(IEnumerable<JobApplication> Items, int Total)> GetByJobAsync(
        Guid jobId, ApplicationStatus? status, int page, int pageSize, CancellationToken ct = default)
    {
        var q = _db.Applications.Where(a => a.JobId == jobId);
        if (status.HasValue) q = q.Where(a => a.Status == status);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.AppliedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }
}
