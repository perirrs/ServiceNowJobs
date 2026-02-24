using SNHub.Auth.Domain.Entities;
using SNHub.Auth.Domain.Enums;

namespace SNHub.Auth.Application.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> GetByEmailWithTokensAsync(string email, CancellationToken ct = default);
    Task<User?> GetByIdWithTokensAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByRefreshTokenAsync(string token, CancellationToken ct = default);
    Task<User?> GetByLinkedInIdAsync(string linkedInId, CancellationToken ct = default);
    Task<User?> GetByAzureAdObjectIdAsync(string objectId, CancellationToken ct = default);
    Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task AddRefreshTokenAsync(RefreshToken token, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);
    Task<(IEnumerable<User> Users, int TotalCount)> GetPagedAsync(
        int page, int pageSize,
        UserRole? roleFilter = null,
        bool? isActiveFilter = null,
        string? searchTerm = null,
        CancellationToken ct = default);
}

public interface ITokenService
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
    Task BlacklistAccessTokenAsync(string jti, DateTimeOffset expiry, CancellationToken ct = default);
    Task<bool> IsTokenBlacklistedAsync(string jti, CancellationToken ct = default);
}

public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string hash);
}

public interface IEmailService
{
    Task SendEmailVerificationAsync(string email, string firstName, string token, CancellationToken ct = default);
    Task SendPasswordResetAsync(string email, string firstName, string token, CancellationToken ct = default);
    Task SendWelcomeEmailAsync(string email, string firstName, CancellationToken ct = default);
    Task SendAccountSuspendedAsync(string email, string firstName, string reason, CancellationToken ct = default);
}

public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Email { get; }
    IEnumerable<string> Roles { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
    bool IsAuthenticated { get; }
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitTransactionAsync(CancellationToken ct = default);
    Task RollbackTransactionAsync(CancellationToken ct = default);
}

public interface IBlobStorageService
{
    Task<string> UploadAsync(Stream content, string fileName, string contentType, CancellationToken ct = default);
    Task DeleteAsync(string blobUrl, CancellationToken ct = default);
    Task<Stream> DownloadAsync(string blobUrl, CancellationToken ct = default);
}
