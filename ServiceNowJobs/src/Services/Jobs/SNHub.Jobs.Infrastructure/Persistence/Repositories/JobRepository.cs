using Microsoft.EntityFrameworkCore;
using SNHub.Jobs.Application.Interfaces;
using SNHub.Jobs.Domain.Entities;
using SNHub.Jobs.Domain.Enums;

namespace SNHub.Jobs.Infrastructure.Persistence.Repositories;

public sealed class JobRepository : IJobRepository
{
    private readonly JobsDbContext _db;
    public JobRepository(JobsDbContext db) { _db = db; }

    public Task<Job?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Jobs.FindAsync([id], ct).AsTask();

    public async Task AddAsync(Job job, CancellationToken ct = default)
        => await _db.Jobs.AddAsync(job, ct);

    public Task UpdateAsync(Job job, CancellationToken ct = default)
    {
        _db.Jobs.Update(job);
        return Task.CompletedTask;
    }

    public async Task<(IEnumerable<Job> Items, int Total)> SearchAsync(
        string? keyword, string? country, string? location,
        JobType? jobType, WorkMode? workMode, ExperienceLevel? level,
        decimal? salaryMin, decimal? salaryMax,
        JobStatus? status, Guid? employerId,
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.Jobs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(j =>
                EF.Functions.ILike(j.Title, $"%{keyword}%") ||
                EF.Functions.ILike(j.Description, $"%{keyword}%"));

        if (!string.IsNullOrWhiteSpace(country))
            query = query.Where(j => j.Country == country);

        if (!string.IsNullOrWhiteSpace(location))
            query = query.Where(j => j.Location != null && EF.Functions.ILike(j.Location, $"%{location}%"));

        if (jobType.HasValue)  query = query.Where(j => j.JobType == jobType);
        if (workMode.HasValue) query = query.Where(j => j.WorkMode == workMode);
        if (level.HasValue)    query = query.Where(j => j.ExperienceLevel == level);
        if (status.HasValue)   query = query.Where(j => j.Status == status);
        if (employerId.HasValue) query = query.Where(j => j.EmployerId == employerId);

        // Salary filtering — only filters against visible salaries
        if (salaryMin.HasValue)
            query = query.Where(j => j.IsSalaryVisible && j.SalaryMax >= salaryMin);
        if (salaryMax.HasValue)
            query = query.Where(j => j.IsSalaryVisible && j.SalaryMin <= salaryMax);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }
}
