using SNHub.Applications.Domain.Entities;
using SNHub.Applications.Domain.Enums;

namespace SNHub.Applications.Application.Interfaces;

public interface IApplicationRepository
{
    Task<JobApplication?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid jobId, Guid candidateId, CancellationToken ct = default);
    Task AddAsync(JobApplication application, CancellationToken ct = default);
    Task<(IEnumerable<JobApplication> Items, int Total)> GetByCandidateAsync(
        Guid candidateId, int page, int pageSize, CancellationToken ct = default);
    Task<(IEnumerable<JobApplication> Items, int Total)> GetByJobAsync(
        Guid jobId, ApplicationStatus? status, int page, int pageSize, CancellationToken ct = default);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
