using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.CvParser.Application.DTOs;
using SNHub.CvParser.Application.Interfaces;
using SNHub.CvParser.Application.Mappers;
using SNHub.CvParser.Domain.Entities;
using SNHub.CvParser.Domain.Exceptions;

namespace SNHub.CvParser.Application.Commands.ParseCv;

public sealed record ParseCvCommand(
    Guid   UserId,
    Stream Content,
    string FileName,
    string ContentType,
    long   FileSizeBytes) : IRequest<CvParseResultDto>;

public sealed class ParseCvCommandValidator : AbstractValidator<ParseCvCommand>
{
    private static readonly string[] Allowed = ["application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document"];
    private const long MaxBytes = 10 * 1024 * 1024; // 10 MB

    public ParseCvCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.ContentType)
            .Must(t => Allowed.Contains(t))
            .WithMessage("Only PDF and DOCX files are supported.");
        RuleFor(x => x.FileSizeBytes)
            .GreaterThan(0).WithMessage("File must not be empty.")
            .LessThanOrEqualTo(MaxBytes).WithMessage("File must be 10 MB or smaller.");
    }
}

public sealed class ParseCvCommandHandler : IRequestHandler<ParseCvCommand, CvParseResultDto>
{
    private readonly ICvParseResultRepository _repo;
    private readonly IUnitOfWork              _uow;
    private readonly IBlobStorageService      _blob;
    private readonly ICvParserService         _parser;
    private readonly ILogger<ParseCvCommandHandler> _logger;

    public ParseCvCommandHandler(
        ICvParseResultRepository repo, IUnitOfWork uow,
        IBlobStorageService blob, ICvParserService parser,
        ILogger<ParseCvCommandHandler> logger)
    { _repo = repo; _uow = uow; _blob = blob; _parser = parser; _logger = logger; }

    public async Task<CvParseResultDto> Handle(ParseCvCommand req, CancellationToken ct)
    {
        // 1. Upload to blob storage
        var ext      = Path.GetExtension(req.FileName).ToLowerInvariant();
        var blobPath = $"cvs/{req.UserId}/{Guid.NewGuid()}{ext}";
        var blobUrl  = await _blob.UploadAsync(req.Content, blobPath, req.ContentType, ct);

        // 2. Create parse result record
        var result = CvParseResult.Create(
            req.UserId, blobPath, req.FileName, req.ContentType, req.FileSizeBytes);
        await _repo.AddAsync(result, ct);
        result.SetProcessing();
        await _uow.SaveChangesAsync(ct);

        try
        {
            // 3. Download blob for parsing (stream already consumed above)
            var blobStream = await _blob.DownloadAsync(blobPath, ct);

            // 4. Call AI parser
            var parsed = await _parser.ParseAsync(blobStream, req.ContentType, ct);

            // 5. Store extracted data
            result.SetCompleted(
                parsed.FirstName, parsed.LastName, parsed.Email, parsed.Phone,
                parsed.Location, parsed.Headline, parsed.Summary, parsed.CurrentRole,
                parsed.YearsOfExperience, parsed.LinkedInUrl, parsed.GitHubUrl,
                JsonSerializer.Serialize(parsed.Skills),
                JsonSerializer.Serialize(parsed.Certifications),
                JsonSerializer.Serialize(parsed.ServiceNowVersions),
                parsed.OverallConfidence,
                JsonSerializer.Serialize(parsed.FieldConfidences));

            _logger.LogInformation(
                "CV parsed for user {UserId}: confidence={Confidence}%, skills={SkillCount}",
                req.UserId, parsed.OverallConfidence, parsed.Skills.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CV parse failed for user {UserId}", req.UserId);
            result.SetFailed(ex.Message);
        }

        await _uow.SaveChangesAsync(ct);
        return CvParseResultMapper.ToDto(result);
    }
}
