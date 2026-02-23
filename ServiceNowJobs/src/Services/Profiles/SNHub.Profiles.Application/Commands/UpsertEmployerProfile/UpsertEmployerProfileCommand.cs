using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Profiles.Application.DTOs;
using SNHub.Profiles.Application.Interfaces;
using SNHub.Profiles.Domain.Entities;
namespace SNHub.Profiles.Application.Commands.UpsertEmployerProfile;
public sealed record UpsertEmployerProfileCommand(Guid UserId, string? CompanyName, string? CompanyDescription, string? Industry, string? CompanySize, string? City, string? Country, string? WebsiteUrl, string? LinkedInUrl) : IRequest<EmployerProfileDto>;
public sealed class UpsertEmployerProfileCommandHandler : IRequestHandler<UpsertEmployerProfileCommand, EmployerProfileDto>
{
    private readonly IEmployerProfileRepository _repo; private readonly IUnitOfWork _uow; private readonly ILogger<UpsertEmployerProfileCommandHandler> _logger;
    public UpsertEmployerProfileCommandHandler(IEmployerProfileRepository repo, IUnitOfWork uow, ILogger<UpsertEmployerProfileCommandHandler> logger) => (_repo, _uow, _logger) = (repo, uow, logger);
    public async Task<EmployerProfileDto> Handle(UpsertEmployerProfileCommand req, CancellationToken ct)
    {
        var profile = await _repo.GetByUserIdAsync(req.UserId, ct) ?? EmployerProfile.Create(req.UserId);
        var isNew = profile.CompanyName == null;
        profile.Update(req.CompanyName, req.CompanyDescription, req.Industry, req.CompanySize, req.City, req.Country, req.WebsiteUrl, req.LinkedInUrl);
        if (isNew) await _repo.AddAsync(profile, ct);
        await _uow.SaveChangesAsync(ct);
        return ToDto(profile);
    }
    public static EmployerProfileDto ToDto(EmployerProfile p) => new(p.Id, p.UserId, p.CompanyName, p.CompanyDescription, p.Industry, p.CompanySize, p.HeadquartersCity, p.HeadquartersCountry, p.WebsiteUrl, p.LinkedInUrl, p.LogoUrl, p.IsVerified, p.CreatedAt, p.UpdatedAt);
}
