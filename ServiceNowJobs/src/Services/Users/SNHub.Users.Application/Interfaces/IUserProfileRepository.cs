using SNHub.Users.Domain.Entities;

namespace SNHub.Users.Application.Interfaces;

public interface IUserProfileRepository
{
    Task<UserProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<UserProfile?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<(IEnumerable<UserProfile> Items, int Total)> GetPagedAsync(
        string? search, bool? isDeleted, int page, int pageSize, CancellationToken ct = default);
    Task AddAsync(UserProfile profile, CancellationToken ct = default);
    Task UpdateAsync(UserProfile profile, CancellationToken ct = default);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IBlobStorageService
{
    Task<string> UploadAsync(Stream content, string path, string contentType, CancellationToken ct = default);
    Task DeleteAsync(string blobUrl, CancellationToken ct = default);
}

public interface ICurrentUserService
{
    Guid? UserId { get; }
    IEnumerable<string> Roles { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
}
