using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.CvParser.Application.DTOs;
using SNHub.CvParser.Application.Interfaces;
using SNHub.CvParser.Domain.Exceptions;

namespace SNHub.CvParser.Application.Commands.ApplyParsedCv;

/// <summary>
/// Applies the AI-extracted fields from a completed parse result into the
/// caller's candidate profile (via HTTP call to the Profiles service).
/// Only fields above the confidence threshold are applied.
/// </summary>
public sealed record ApplyParsedCvCommand(
    Guid ParseResultId,
    Guid RequesterId,
    int  ConfidenceThreshold = 60) : IRequest<ApplyParsedCvResponse>;

public sealed class ApplyParsedCvCommandValidator : AbstractValidator<ApplyParsedCvCommand>
{
    public ApplyParsedCvCommandValidator()
    {
        RuleFor(x => x.ParseResultId).NotEmpty();
        RuleFor(x => x.RequesterId).NotEmpty();
        RuleFor(x => x.ConfidenceThreshold).InclusiveBetween(0, 100);
    }
}

public sealed class ApplyParsedCvCommandHandler : IRequestHandler<ApplyParsedCvCommand, ApplyParsedCvResponse>
{
    private readonly ICvParseResultRepository  _repo;
    private readonly IUnitOfWork               _uow;
    private readonly IProfilesServiceClient    _profiles;
    private readonly ILogger<ApplyParsedCvCommandHandler> _logger;

    public ApplyParsedCvCommandHandler(
        ICvParseResultRepository repo, IUnitOfWork uow,
        IProfilesServiceClient profiles,
        ILogger<ApplyParsedCvCommandHandler> logger)
    { _repo = repo; _uow = uow; _profiles = profiles; _logger = logger; }

    public async Task<ApplyParsedCvResponse> Handle(ApplyParsedCvCommand req, CancellationToken ct)
    {
        var result = await _repo.GetByIdAsync(req.ParseResultId, ct)
            ?? throw new ParseResultNotFoundException(req.ParseResultId);

        if (result.UserId != req.RequesterId)
            throw new ParseResultAccessDeniedException();

        if (result.Status != Domain.Enums.ParseStatus.Completed)
            throw new ParseNotCompletedException(req.ParseResultId);

        if (result.IsApplied)
            throw new ParseAlreadyAppliedException(req.ParseResultId);

        // Build the patch — only include fields above confidence threshold
        var confidences = DeserializeConfidences(result.FieldConfidencesJson);
        var patch = BuildProfilePatch(result, confidences, req.ConfidenceThreshold);

        // Push to Profiles service
        await _profiles.ApplyParsedDataAsync(req.RequesterId, patch, ct);

        result.MarkApplied();
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Parse result {ParseResultId} applied for user {UserId}",
            req.ParseResultId, req.RequesterId);

        return new ApplyParsedCvResponse(
            req.ParseResultId, true,
            "CV data successfully applied to your profile.");
    }

    private static ProfilePatch BuildProfilePatch(
        Domain.Entities.CvParseResult r,
        Dictionary<string, int> confidences, int threshold)
    {
        static bool Ok(Dictionary<string, int> c, string key, int t) =>
            !c.TryGetValue(key, out var score) || score >= t;

        return new ProfilePatch
        {
            Headline          = Ok(confidences, "Headline", threshold) ? r.Headline : null,
            Summary           = Ok(confidences, "Summary", threshold)  ? r.Summary  : null,
            CurrentRole       = Ok(confidences, "CurrentRole", threshold) ? r.CurrentRole : null,
            YearsOfExperience = Ok(confidences, "YearsOfExperience", threshold) ? r.YearsOfExperience : null,
            Location          = Ok(confidences, "Location", threshold) ? r.Location : null,
            LinkedInUrl       = Ok(confidences, "LinkedInUrl", threshold) ? r.LinkedInUrl : null,
            GitHubUrl         = Ok(confidences, "GitHubUrl", threshold) ? r.GitHubUrl : null,
            SkillsJson        = Ok(confidences, "Skills", threshold) ? r.SkillsJson : null,
            CertificationsJson= Ok(confidences, "Certifications", threshold) ? r.CertificationsJson : null,
            ServiceNowVersionsJson = Ok(confidences, "ServiceNowVersions", threshold)
                ? r.ServiceNowVersionsJson : null,
        };
    }

    private static Dictionary<string, int> DeserializeConfidences(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? []; }
        catch { return []; }
    }
}

public sealed class ProfilePatch
{
    public string? Headline           { get; set; }
    public string? Summary            { get; set; }
    public string? CurrentRole        { get; set; }
    public int?    YearsOfExperience  { get; set; }
    public string? Location           { get; set; }
    public string? LinkedInUrl        { get; set; }
    public string? GitHubUrl          { get; set; }
    public string? SkillsJson         { get; set; }
    public string? CertificationsJson { get; set; }
    public string? ServiceNowVersionsJson { get; set; }
}

public interface IProfilesServiceClient
{
    Task ApplyParsedDataAsync(Guid userId, ProfilePatch patch, CancellationToken ct = default);
}
