using SNHub.Profiles.Domain.Entities;
namespace SNHub.Profiles.Application.Interfaces;
public interface ICandidateProfileRepository
{
    Task<CandidateProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(CandidateProfile profile, CancellationToken ct = default);
    Task<(IEnumerable<CandidateProfile> Items, int Total)> SearchAsync(string? keyword, string? country, int? minExperience, bool? openToRemote, int page, int pageSize, CancellationToken ct = default);
}
public interface IEmployerProfileRepository
{
    Task<EmployerProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(EmployerProfile profile, CancellationToken ct = default);
}
public interface IUnitOfWork { Task<int> SaveChangesAsync(CancellationToken ct = default); }
