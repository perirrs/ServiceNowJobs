using SNHub.Jobs.Domain.Entities;
using SNHub.Jobs.Domain.Enums;

namespace SNHub.Jobs.Application.Interfaces;

public interface IJobRepository
{
    Task<Job?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Job job, CancellationToken ct = default);
    Task UpdateAsync(Job job, CancellationToken ct = default);
    Task<(IEnumerable<Job> Items, int Total)> SearchAsync(
        string? keyword, string? country, string? location,
        JobType? jobType, WorkMode? workMode, ExperienceLevel? level,
        decimal? salaryMin, decimal? salaryMax,
        JobStatus? status, Guid? employerId,
        int page, int pageSize, CancellationToken ct = default);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
