using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Users.Application.Interfaces;
using SNHub.Users.Domain.Entities;

namespace SNHub.Users.Application.Commands.UploadProfilePicture;

public sealed record UploadProfilePictureCommand(Guid UserId, Stream Content, string FileName, string ContentType) : IRequest<string>;

public sealed class UploadProfilePictureCommandHandler : IRequestHandler<UploadProfilePictureCommand, string>
{
    private readonly IUserProfileRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly IBlobStorageService _blob;
    private readonly ILogger<UploadProfilePictureCommandHandler> _logger;

    public UploadProfilePictureCommandHandler(IUserProfileRepository repo, IUnitOfWork uow, IBlobStorageService blob, ILogger<UploadProfilePictureCommandHandler> logger)
    { _repo = repo; _uow = uow; _blob = blob; _logger = logger; }

    public async Task<string> Handle(UploadProfilePictureCommand req, CancellationToken ct)
    {
        var fileName = $"profile-pictures/{req.UserId}/{Guid.NewGuid()}{Path.GetExtension(req.FileName)}";
        var url = await _blob.UploadAsync(req.Content, fileName, req.ContentType, ct);

        var profile = await _repo.GetByUserIdAsync(req.UserId, ct) ?? UserProfile.Create(req.UserId);
        profile.SetProfilePicture(url);
        await _repo.UpdateAsync(profile, ct);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Profile picture uploaded for user {UserId}", req.UserId);
        return url;
    }
}
