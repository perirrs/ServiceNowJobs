using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Users.Application.Interfaces;
using SNHub.Users.Domain.Entities;

namespace SNHub.Users.Application.Commands.UploadProfilePicture;

public sealed record UploadProfilePictureCommand(
    Guid   UserId,
    Stream Content,
    string FileName,
    string ContentType,
    long   FileSize) : IRequest<string>;

public sealed class UploadProfilePictureCommandValidator
    : AbstractValidator<UploadProfilePictureCommand>
{
    private static readonly string[] AllowedTypes = ["image/jpeg", "image/png", "image/webp"];
    private const long MaxBytes = 5 * 1024 * 1024; // 5 MB

    public UploadProfilePictureCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.ContentType)
            .Must(t => AllowedTypes.Contains(t))
            .WithMessage("Only JPEG, PNG, and WebP images are allowed.");
        RuleFor(x => x.FileSize)
            .LessThanOrEqualTo(MaxBytes)
            .WithMessage("Image must be 5 MB or smaller.");
    }
}

public sealed class UploadProfilePictureCommandHandler
    : IRequestHandler<UploadProfilePictureCommand, string>
{
    private readonly IUserProfileRepository _repo;
    private readonly IUnitOfWork            _uow;
    private readonly IBlobStorageService    _blob;
    private readonly ILogger<UploadProfilePictureCommandHandler> _logger;

    public UploadProfilePictureCommandHandler(
        IUserProfileRepository repo, IUnitOfWork uow,
        IBlobStorageService blob,
        ILogger<UploadProfilePictureCommandHandler> logger)
    { _repo = repo; _uow = uow; _blob = blob; _logger = logger; }

    public async Task<string> Handle(UploadProfilePictureCommand req, CancellationToken ct)
    {
        var ext  = Path.GetExtension(req.FileName).ToLowerInvariant();
        var path = $"profile-pictures/{req.UserId}/{Guid.NewGuid()}{ext}";
        var url  = await _blob.UploadAsync(req.Content, path, req.ContentType, ct);

        // AddAsync for brand-new profile, UpdateAsync for existing — avoids EF tracking error
        var profile = await _repo.GetByUserIdAsync(req.UserId, ct);
        if (profile is null)
        {
            profile = UserProfile.Create(req.UserId);
            profile.SetProfilePicture(url);
            await _repo.AddAsync(profile, ct);
        }
        else
        {
            profile.SetProfilePicture(url);
            await _repo.UpdateAsync(profile, ct);
        }

        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("Profile picture uploaded for user {UserId}", req.UserId);
        return url;
    }
}
