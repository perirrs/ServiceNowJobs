using SNHub.Profiles.Domain.Entities;
using SNHub.Profiles.Domain.Enums;

namespace SNHub.Profiles.Application.Interfaces;

public interface ICandidateProfileRepository
{
    Task<CandidateProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(CandidateProfile profile, CancellationToken ct = default);
    Task<(IEnumerable<CandidateProfile> Items, int Total)> SearchAsync(
        string? keyword, string? country, ExperienceLevel? level,
        int? minYears, bool? openToRemote, AvailabilityStatus? availability,
        int page, int pageSize, CancellationToken ct = default);
}

public interface IEmployerProfileRepository
{
    Task<EmployerProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(EmployerProfile profile, CancellationToken ct = default);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>
/// File storage abstraction — AzureBlobStorageService in prod, LocalFileStorageService for tests.
/// </summary>
public interface IFileStorageService
{
    Task<string> UploadAsync(Stream content, string path, string contentType, CancellationToken ct = default);
    Task DeleteAsync(string fileUrl, CancellationToken ct = default);
}

public interface ICurrentUserService
{
    Guid? UserId { get; }
    IEnumerable<string> Roles { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
}
