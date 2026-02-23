using SNHub.Jobs.Domain.Entities;
using SNHub.Jobs.Domain.Enums;

namespace SNHub.Jobs.Application.Interfaces;

public interface IJobRepository
{
    Task<Job?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Job job, CancellationToken ct = default);
    Task UpdateAsync(Job job, CancellationToken ct = default);
    Task<(IEnumerable<Job> Items, int Total)> SearchAsync(
        string? keyword, string? country, JobType? jobType,
        WorkMode? workMode, ExperienceLevel? level, JobStatus? status,
        Guid? employerId, int page, int pageSize, CancellationToken ct = default);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
