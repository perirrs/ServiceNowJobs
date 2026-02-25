using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.JobEnhancer.Application.DTOs;
using SNHub.JobEnhancer.Application.Interfaces;
using SNHub.JobEnhancer.Application.Mappers;
using SNHub.JobEnhancer.Domain.Entities;

namespace SNHub.JobEnhancer.Application.Commands.EnhanceDescription;

/// <summary>
/// Sends the job description to GPT-4o for enhancement.
/// Creates an EnhancementResult record synchronously — the employer
/// receives full structured feedback including bias detection,
/// quality scores, and rewritten content in a single response.
/// </summary>
public sealed record EnhanceDescriptionCommand(
    Guid    JobId,
    Guid    RequestedBy,
    string  Title,
    string  Description,
    string? Requirements) : IRequest<EnhancementResultDto>;

public sealed class EnhanceDescriptionCommandValidator
    : AbstractValidator<EnhanceDescriptionCommand>
{
    public EnhanceDescriptionCommandValidator()
    {
        RuleFor(x => x.JobId).NotEmpty();
        RuleFor(x => x.RequestedBy).NotEmpty();
        RuleFor(x => x.Title)
            .NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description)
            .NotEmpty().MinimumLength(50)
            .WithMessage("Description must be at least 50 characters for meaningful enhancement.")
            .MaximumLength(10_000);
        RuleFor(x => x.Requirements)
            .MaximumLength(5_000).When(x => x.Requirements is not null);
    }
}

public sealed class EnhanceDescriptionCommandHandler
    : IRequestHandler<EnhanceDescriptionCommand, EnhancementResultDto>
{
    private readonly IEnhancementResultRepository _repo;
    private readonly IUnitOfWork                  _uow;
    private readonly IJobDescriptionEnhancer      _enhancer;
    private readonly ILogger<EnhanceDescriptionCommandHandler> _logger;

    private static readonly JsonSerializerOptions _json =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public EnhanceDescriptionCommandHandler(
        IEnhancementResultRepository repo, IUnitOfWork uow,
        IJobDescriptionEnhancer enhancer,
        ILogger<EnhanceDescriptionCommandHandler> logger)
    { _repo = repo; _uow = uow; _enhancer = enhancer; _logger = logger; }

    public async Task<EnhancementResultDto> Handle(
        EnhanceDescriptionCommand req, CancellationToken ct)
    {
        var record = EnhancementResult.Create(
            req.JobId, req.RequestedBy, req.Title, req.Description, req.Requirements);
        await _repo.AddAsync(record, ct);
        await _uow.SaveChangesAsync(ct);

        try
        {
            _logger.LogInformation(
                "Enhancing job description for job {JobId}", req.JobId);

            var result = await _enhancer.EnhanceAsync(
                req.Title, req.Description, req.Requirements, ct);

            record.SetCompleted(
                result.EnhancedTitle,
                result.EnhancedDescription,
                result.EnhancedRequirements,
                result.ScoreBefore,
                result.ScoreAfter,
                JsonSerializer.Serialize(result.BiasIssues,      _json),
                JsonSerializer.Serialize(result.MissingFields,   _json),
                JsonSerializer.Serialize(result.Improvements,    _json),
                JsonSerializer.Serialize(result.SuggestedSkills, _json));

            _logger.LogInformation(
                "Enhanced job {JobId}: score {Before}→{After}, {BiasCount} bias issues",
                req.JobId, result.ScoreBefore, result.ScoreAfter,
                result.BiasIssues.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enhance job {JobId}", req.JobId);
            record.SetFailed(ex.Message);
        }

        await _uow.SaveChangesAsync(ct);
        return EnhancementResultMapper.ToDto(record);
    }
}
