using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Profiles.Application.Interfaces;
using SNHub.Profiles.Domain.Entities;
using SNHub.Profiles.Domain.Exceptions;

namespace SNHub.Profiles.Application.Commands.UploadFile;

// ── Upload profile picture ────────────────────────────────────────────────────

public sealed record UploadProfilePictureCommand(
    Guid UserId,
    Stream Content,
    string FileName,
    string ContentType,
    long FileSizeBytes) : IRequest<string>;

public sealed class UploadProfilePictureCommandHandler
    : IRequestHandler<UploadProfilePictureCommand, string>
{
    private static readonly HashSet<string> _allowed = ["image/jpeg", "image/png", "image/webp"];
    private const int MaxMb = 5;

    private readonly ICandidateProfileRepository _repo;
    private readonly IFileStorageService _storage;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<UploadProfilePictureCommandHandler> _logger;

    public UploadProfilePictureCommandHandler(
        ICandidateProfileRepository repo, IFileStorageService storage,
        IUnitOfWork uow, ILogger<UploadProfilePictureCommandHandler> logger)
    { _repo = repo; _storage = storage; _uow = uow; _logger = logger; }

    public async Task<string> Handle(UploadProfilePictureCommand req, CancellationToken ct)
    {
        if (!_allowed.Contains(req.ContentType.ToLower()))
            throw new InvalidFileTypeException("JPEG, PNG, WebP");
        if (req.FileSizeBytes > MaxMb * 1024 * 1024)
            throw new FileTooLargeException(MaxMb);

        var ext  = Path.GetExtension(req.FileName).ToLower();
        var path = $"profile-pictures/{req.UserId}/{Guid.NewGuid()}{ext}";
        var url  = await _storage.UploadAsync(req.Content, path, req.ContentType, ct);

        var profile = await _repo.GetByUserIdAsync(req.UserId, ct) ?? CandidateProfile.Create(req.UserId);
        var isNew   = profile.ProfilePictureUrl is null && profile.Headline is null;
        profile.SetProfilePicture(url);
        if (isNew) await _repo.AddAsync(profile, ct);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Profile picture uploaded for {UserId}: {Path}", req.UserId, path);
        return url;
    }
}

// ── Upload CV ─────────────────────────────────────────────────────────────────

public sealed record UploadCvCommand(
    Guid UserId,
    Stream Content,
    string FileName,
    string ContentType,
    long FileSizeBytes) : IRequest<string>;

public sealed class UploadCvCommandHandler : IRequestHandler<UploadCvCommand, string>
{
    private static readonly HashSet<string> _allowed = ["application/pdf"];
    private const int MaxMb = 10;

    private readonly ICandidateProfileRepository _repo;
    private readonly IFileStorageService _storage;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<UploadCvCommandHandler> _logger;

    public UploadCvCommandHandler(
        ICandidateProfileRepository repo, IFileStorageService storage,
        IUnitOfWork uow, ILogger<UploadCvCommandHandler> logger)
    { _repo = repo; _storage = storage; _uow = uow; _logger = logger; }

    public async Task<string> Handle(UploadCvCommand req, CancellationToken ct)
    {
        if (!_allowed.Contains(req.ContentType.ToLower()))
            throw new InvalidFileTypeException("PDF");
        if (req.FileSizeBytes > MaxMb * 1024 * 1024)
            throw new FileTooLargeException(MaxMb);

        var path = $"cvs/{req.UserId}/{Guid.NewGuid()}.pdf";
        var url  = await _storage.UploadAsync(req.Content, path, req.ContentType, ct);

        var profile = await _repo.GetByUserIdAsync(req.UserId, ct) ?? CandidateProfile.Create(req.UserId);
        var isNew   = profile.CvUrl is null && profile.Headline is null;
        profile.SetCvUrl(url);
        if (isNew) await _repo.AddAsync(profile, ct);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("CV uploaded for {UserId}: {Path}", req.UserId, path);
        return url;
    }
}
