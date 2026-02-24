using FluentValidation;
using MediatR;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Domain.Exceptions;

namespace SNHub.Auth.Application.Commands.UploadProfilePicture;

public sealed record UploadProfilePictureCommand(
    byte[] FileBytes,
    string FileName,
    string ContentType) : IRequest<string>;

public sealed class UploadProfilePictureCommandValidator : AbstractValidator<UploadProfilePictureCommand>
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp"
    };

    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    public UploadProfilePictureCommandValidator()
    {
        RuleFor(x => x.FileBytes)
            .NotEmpty().WithMessage("File is required.")
            .Must(b => b.Length <= MaxFileSizeBytes)
            .WithMessage("File size must not exceed 5 MB.");

        RuleFor(x => x.ContentType)
            .NotEmpty()
            .Must(ct => AllowedContentTypes.Contains(ct))
            .WithMessage("Only JPEG, PNG, and WebP images are allowed.");

        RuleFor(x => x.FileName)
            .NotEmpty()
            .MaximumLength(255);
    }
}

public sealed class UploadProfilePictureCommandHandler
    : IRequestHandler<UploadProfilePictureCommand, string>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly IBlobStorageService _blob;
    private readonly ICurrentUserService _currentUser;

    public UploadProfilePictureCommandHandler(
        IUserRepository users, IUnitOfWork uow,
        IBlobStorageService blob, ICurrentUserService currentUser)
    {
        _users = users;
        _uow = uow;
        _blob = blob;
        _currentUser = currentUser;
    }

    public async Task<string> Handle(UploadProfilePictureCommand request, CancellationToken ct)
    {
        var email = _currentUser.Email
            ?? throw new UnauthorizedAccessException("User not authenticated.");

        var user = await _users.GetByEmailAsync(email, ct)
            ?? throw new UserNotFoundException($"User with email {email} not found.");

        // Delete old picture if one exists
        if (!string.IsNullOrWhiteSpace(user.ProfilePictureUrl))
        {
            try { await _blob.DeleteAsync(user.ProfilePictureUrl, ct); }
            catch { /* non-critical — old blob cleanup */ }
        }

        var blobName = $"profile-pictures/{user.Id}/{Guid.NewGuid()}{GetExtension(request.ContentType)}";

        using var stream = new MemoryStream(request.FileBytes);
        var url = await _blob.UploadAsync(stream, blobName, request.ContentType, ct);

        user.UpdateProfilePicture(url);
        await _uow.SaveChangesAsync(ct);

        return url;
    }

    private static string GetExtension(string contentType) => contentType.ToLower() switch
    {
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/png"                  => ".png",
        "image/webp"                 => ".webp",
        _                            => ".jpg"
    };
}
