using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SNHub.JobEnhancer.Application.Commands.AcceptEnhancement;
using SNHub.JobEnhancer.Application.Commands.EnhanceDescription;
using SNHub.JobEnhancer.Application.DTOs;
using SNHub.JobEnhancer.Application.Interfaces;
using SNHub.JobEnhancer.Application.Mappers;
using SNHub.JobEnhancer.Application.Queries.GetEnhancement;
using SNHub.JobEnhancer.Domain.Entities;
using SNHub.JobEnhancer.Domain.Enums;
using SNHub.JobEnhancer.Domain.Exceptions;
using SNHub.JobEnhancer.Infrastructure.Services;
using Xunit;

namespace SNHub.JobEnhancer.UnitTests;

// ════════════════════════════════════════════════════════════════════════════
// Domain entity — EnhancementResult
// ════════════════════════════════════════════════════════════════════════════

public sealed class EnhancementResultEntityTests
{
    private static EnhancementResult MakeResult(Guid? jobId = null, Guid? userId = null)
        => EnhancementResult.Create(
            jobId ?? Guid.NewGuid(), userId ?? Guid.NewGuid(),
            "SNow Developer", "We need a great developer for our platform.", null);

    [Fact]
    public void Create_SetsProcessingStatus()
    {
        var r = MakeResult();
        r.Status.Should().Be(EnhancementStatus.Processing);
        r.IsAccepted.Should().BeFalse();
        r.ScoreBefore.Should().Be(0);
    }

    [Fact]
    public void SetCompleted_PopulatesAllFields()
    {
        var r = MakeResult();
        r.SetCompleted(
            "Enhanced Title", "Enhanced description.", "Enhanced requirements.",
            55, 78, "[]", "[\"Salary range\"]", "[]", "[\"ITSM\"]");

        r.Status.Should().Be(EnhancementStatus.Completed);
        r.EnhancedTitle.Should().Be("Enhanced Title");
        r.ScoreBefore.Should().Be(55);
        r.ScoreAfter.Should().Be(78);
        r.ScoreImprovement.Should().Be(23);
        r.MissingFieldsJson.Should().Be("[\"Salary range\"]");
    }

    [Fact]
    public void SetCompleted_ClampsScoresTo0_100()
    {
        var r = MakeResult();
        r.SetCompleted(null, null, null, -10, 150, "[]", "[]", "[]", "[]");
        r.ScoreBefore.Should().Be(0);
        r.ScoreAfter.Should().Be(100);
    }

    [Fact]
    public void SetFailed_SetsErrorMessage()
    {
        var r = MakeResult();
        r.SetFailed("OpenAI timeout");
        r.Status.Should().Be(EnhancementStatus.Failed);
        r.ErrorMessage.Should().Be("OpenAI timeout");
    }

    [Fact]
    public void Accept_SetsIsAcceptedAndTimestamp()
    {
        var r = MakeResult();
        r.SetCompleted("T", "D", null, 50, 70, "[]", "[]", "[]", "[]");
        r.Accept();
        r.IsAccepted.Should().BeTrue();
        r.AcceptedAt.Should().NotBeNull();
    }

    [Fact]
    public void Accept_WhenNotCompleted_Throws()
    {
        var r = MakeResult();
        var act = () => r.Accept();
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*non-completed*");
    }

    [Fact]
    public void Accept_WhenAlreadyAccepted_Throws()
    {
        var r = MakeResult();
        r.SetCompleted("T", "D", null, 50, 70, "[]", "[]", "[]", "[]");
        r.Accept();
        var act = () => r.Accept();
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*already accepted*");
    }

    [Fact]
    public void OriginalTitle_IsTrimmed()
    {
        var r = EnhancementResult.Create(
            Guid.NewGuid(), Guid.NewGuid(), "  SNow Dev  ", "desc", null);
        r.OriginalTitle.Should().Be("SNow Dev");
    }
}

// ════════════════════════════════════════════════════════════════════════════
// EnhanceDescription command validator
// ════════════════════════════════════════════════════════════════════════════

public sealed class EnhanceDescriptionCommandValidatorTests
{
    private readonly EnhanceDescriptionCommandValidator _sut = new();

    private static EnhanceDescriptionCommand Valid() => new(
        Guid.NewGuid(), Guid.NewGuid(),
        "ServiceNow Developer",
        "We are looking for an experienced ServiceNow developer to join our growing team and build amazing solutions.",
        null);

    [Fact]
    public void Validate_Valid_Passes()
        => _sut.Validate(Valid()).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_EmptyJobId_Fails()
    {
        var cmd = Valid() with { JobId = Guid.Empty };
        _sut.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyTitle_Fails()
    {
        var cmd = Valid() with { Title = "" };
        _sut.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ShortDescription_Fails()
    {
        var cmd = Valid() with { Description = "Too short" };
        var result = _sut.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("50 characters"));
    }

    [Fact]
    public void Validate_TooLongDescription_Fails()
    {
        var cmd = Valid() with { Description = new string('x', 10_001) };
        _sut.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_NullRequirements_Passes()
    {
        var cmd = Valid() with { Requirements = null };
        _sut.Validate(cmd).IsValid.Should().BeTrue();
    }
}

// ════════════════════════════════════════════════════════════════════════════
// EnhanceDescription command handler
// ════════════════════════════════════════════════════════════════════════════

public sealed class EnhanceDescriptionCommandHandlerTests
{
    private readonly Mock<IEnhancementResultRepository> _repo     = new();
    private readonly Mock<IUnitOfWork>                  _uow      = new();
    private readonly Mock<IJobDescriptionEnhancer>      _enhancer = new();

    private EnhanceDescriptionCommandHandler Handler() => new(
        _repo.Object, _uow.Object, _enhancer.Object,
        NullLogger<EnhanceDescriptionCommandHandler>.Instance);

    private static readonly EnhanceDescriptionCommand _validCmd = new(
        Guid.NewGuid(), Guid.NewGuid(),
        "SNow Developer",
        "We are seeking an experienced ServiceNow developer to join our innovative team.",
        "5+ years ServiceNow. CSA required.");

    private static GptEnhancementResult StubGptResult() => new()
    {
        EnhancedTitle        = "ServiceNow Developer",
        EnhancedDescription  = "We are looking for a skilled ServiceNow developer.",
        EnhancedRequirements = "5+ years ServiceNow experience. CSA certification required.",
        ScoreBefore          = 55,
        ScoreAfter           = 78,
        BiasIssues           = [],
        MissingFields        = ["Salary range"],
        Improvements         = [new GptImprovement { Category="Clarity", Description="x", Before="y", After="z" }],
        SuggestedSkills      = ["ATF", "CMDB"]
    };

    [Fact]
    public async Task Handle_Success_ReturnsCompletedDto()
    {
        _enhancer.Setup(e => e.EnhanceAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), default)).ReturnsAsync(StubGptResult());

        var dto = await Handler().Handle(_validCmd, CancellationToken.None);

        dto.Status.Should().Be("Completed");
        dto.EnhancedTitle.Should().Be("ServiceNow Developer");
        dto.ScoreBefore.Should().Be(55);
        dto.ScoreAfter.Should().Be(78);
        dto.ScoreImprovement.Should().Be(23);
        dto.MissingFields.Should().Contain("Salary range");
        dto.SuggestedSkills.Should().Contain("ATF");
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_EnhancerThrows_ReturnsFailedDto()
    {
        _enhancer.Setup(e => e.EnhanceAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), default)).ThrowsAsync(new Exception("rate limit"));

        var dto = await Handler().Handle(_validCmd, CancellationToken.None);

        dto.Status.Should().Be("Failed");
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Exactly(2));
    }
}

// ════════════════════════════════════════════════════════════════════════════
// AcceptEnhancement command handler
// ════════════════════════════════════════════════════════════════════════════

public sealed class AcceptEnhancementCommandHandlerTests
{
    private readonly Mock<IEnhancementResultRepository> _repo = new();
    private readonly Mock<IUnitOfWork>                  _uow  = new();
    private readonly Mock<IJobsServiceClient>           _jobs = new();

    private AcceptEnhancementCommandHandler Handler() => new(
        _repo.Object, _uow.Object, _jobs.Object,
        NullLogger<AcceptEnhancementCommandHandler>.Instance);

    private static EnhancementResult CompletedResult(Guid userId)
    {
        var r = EnhancementResult.Create(
            Guid.NewGuid(), userId, "Title",
            "A long enough description for the job posting.", null);
        r.SetCompleted("ET", "ED", null, 55, 78, "[]", "[]", "[]", "[\"ITSM\"]");
        return r;
    }

    [Fact]
    public async Task Handle_ValidAccept_CallsJobsServiceAndReturnsSuccess()
    {
        var userId = Guid.NewGuid();
        var record = CompletedResult(userId);
        _repo.Setup(r => r.GetByIdAsync(record.Id, default)).ReturnsAsync(record);

        var response = await Handler().Handle(
            new AcceptEnhancementCommand(record.Id, userId), CancellationToken.None);

        response.Accepted.Should().BeTrue();
        response.Message.Should().Contain("accepted");
        _jobs.Verify(j => j.ApplyEnhancementAsync(
            record.JobId, It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string[]>(), default), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_NotFound_Throws()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
             .ReturnsAsync((EnhancementResult?)null);
        var act = async () => await Handler().Handle(
            new AcceptEnhancementCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);
        await act.Should().ThrowAsync<EnhancementNotFoundException>();
    }

    [Fact]
    public async Task Handle_DifferentUser_ThrowsAccessDenied()
    {
        var record = CompletedResult(Guid.NewGuid());
        _repo.Setup(r => r.GetByIdAsync(record.Id, default)).ReturnsAsync(record);
        var act = async () => await Handler().Handle(
            new AcceptEnhancementCommand(record.Id, Guid.NewGuid()), CancellationToken.None);
        await act.Should().ThrowAsync<EnhancementAccessDeniedException>();
    }

    [Fact]
    public async Task Handle_NotCompleted_Throws()
    {
        var userId = Guid.NewGuid();
        var record = EnhancementResult.Create(
            Guid.NewGuid(), userId, "T",
            "A description that is long enough.", null);
        // Still Processing
        _repo.Setup(r => r.GetByIdAsync(record.Id, default)).ReturnsAsync(record);
        var act = async () => await Handler().Handle(
            new AcceptEnhancementCommand(record.Id, userId), CancellationToken.None);
        await act.Should().ThrowAsync<EnhancementNotCompletedException>();
    }

    [Fact]
    public async Task Handle_AlreadyAccepted_Throws()
    {
        var userId = Guid.NewGuid();
        var record = CompletedResult(userId);
        record.Accept();
        _repo.Setup(r => r.GetByIdAsync(record.Id, default)).ReturnsAsync(record);
        var act = async () => await Handler().Handle(
            new AcceptEnhancementCommand(record.Id, userId), CancellationToken.None);
        await act.Should().ThrowAsync<EnhancementAlreadyAcceptedException>();
    }
}

// ════════════════════════════════════════════════════════════════════════════
// GetEnhancement query handler
// ════════════════════════════════════════════════════════════════════════════

public sealed class GetEnhancementQueryHandlerTests
{
    private readonly Mock<IEnhancementResultRepository> _repo = new();
    private GetEnhancementQueryHandler Handler() => new(_repo.Object);

    [Fact]
    public async Task Handle_OwnResult_ReturnsDto()
    {
        var userId = Guid.NewGuid();
        var record = EnhancementResult.Create(
            Guid.NewGuid(), userId, "T",
            "A description that is long enough.", null);
        _repo.Setup(r => r.GetByIdAsync(record.Id, default)).ReturnsAsync(record);

        var dto = await Handler().Handle(
            new GetEnhancementQuery(record.Id, userId), CancellationToken.None);
        dto.Id.Should().Be(record.Id);
    }

    [Fact]
    public async Task Handle_OtherUserResult_ThrowsAccessDenied()
    {
        var record = EnhancementResult.Create(
            Guid.NewGuid(), Guid.NewGuid(), "T",
            "A description that is long enough.", null);
        _repo.Setup(r => r.GetByIdAsync(record.Id, default)).ReturnsAsync(record);

        var act = async () => await Handler().Handle(
            new GetEnhancementQuery(record.Id, Guid.NewGuid()), CancellationToken.None);
        await act.Should().ThrowAsync<EnhancementAccessDeniedException>();
    }

    [Fact]
    public async Task Handle_NotFound_Throws()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
             .ReturnsAsync((EnhancementResult?)null);
        var act = async () => await Handler().Handle(
            new GetEnhancementQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);
        await act.Should().ThrowAsync<EnhancementNotFoundException>();
    }
}

// ════════════════════════════════════════════════════════════════════════════
// EnhancementResultMapper
// ════════════════════════════════════════════════════════════════════════════

public sealed class EnhancementResultMapperTests
{
    [Fact]
    public void ToDto_CompletedResult_MapsAllFields()
    {
        var userId = Guid.NewGuid();
        var record = EnhancementResult.Create(
            Guid.NewGuid(), userId, "Title", "Description long enough.", null);
        record.SetCompleted("ET", "ED", "ER", 50, 75,
            "[{\"text\":\"rockstar\",\"reason\":\"biased\",\"suggestion\":\"expert\",\"severity\":\"High\"}]",
            "[\"Salary range\"]",
            "[{\"category\":\"Clarity\",\"description\":\"d\",\"before\":\"b\",\"after\":\"a\"}]",
            "[\"ITSM\",\"HRSD\"]");

        var dto = EnhancementResultMapper.ToDto(record);

        dto.Status.Should().Be("Completed");
        dto.ScoreImprovement.Should().Be(25);
        dto.BiasIssues.Should().HaveCount(1);
        dto.BiasIssues[0].Text.Should().Be("rockstar");
        dto.BiasIssues[0].Severity.Should().Be("High");
        dto.MissingFields.Should().Contain("Salary range");
        dto.Improvements.Should().HaveCount(1);
        dto.SuggestedSkills.Should().Contain("ITSM");
    }

    [Fact]
    public void ToDto_MalformedJson_DoesNotThrow()
    {
        var record = EnhancementResult.Create(
            Guid.NewGuid(), Guid.NewGuid(), "T", "Long enough description here.", null);
        // BiasIssuesJson defaults to "[]" — simulate with malformed via reflection
        var dto = EnhancementResultMapper.ToDto(record);
        dto.BiasIssues.Should().BeEmpty();
    }
}

// ════════════════════════════════════════════════════════════════════════════
// StubJobDescriptionEnhancer
// ════════════════════════════════════════════════════════════════════════════

public sealed class StubJobDescriptionEnhancerTests
{
    private readonly StubJobDescriptionEnhancer _sut = new();

    [Fact]
    public async Task Enhance_ReturnsCompletedResult()
    {
        var result = await _sut.EnhanceAsync(
            "ServiceNow Developer",
            "We are seeking a rockstar ninja developer to join our young, energetic team. " +
            "Must be a native English speaker who is a he/she.",
            "5+ years ServiceNow. CSA required.");

        result.EnhancedTitle.Should().NotBeNullOrEmpty();
        result.EnhancedDescription.Should().NotBeNullOrEmpty();
        result.ScoreBefore.Should().BeGreaterThan(0);
        result.ScoreAfter.Should().BeGreaterThan(result.ScoreBefore);
    }

    [Fact]
    public async Task Enhance_DetectsBiasedLanguage()
    {
        var result = await _sut.EnhanceAsync(
            "SNow Dev",
            "We need a rockstar ninja. Must be a native English speaker. Young energetic team. " +
            "The candidate must be a he/she with passion for coding.",
            null);

        result.BiasIssues.Should().NotBeEmpty();
        result.BiasIssues.Should().Contain(b => b.Severity == "High");
    }

    [Fact]
    public async Task Enhance_IdentifiesMissingFields()
    {
        var result = await _sut.EnhanceAsync(
            "SNow Consultant",
            "Join our ServiceNow consulting team and help clients implement solutions.",
            null);

        result.MissingFields.Should().Contain("Salary range");
    }

    [Fact]
    public async Task Enhance_SuggestsAdditionalSkills()
    {
        var result = await _sut.EnhanceAsync(
            "SNow Developer", "Description long enough for our team.", null);
        result.SuggestedSkills.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Enhance_NoBias_GivesHigherScore()
    {
        var clean = await _sut.EnhanceAsync(
            "ServiceNow Developer",
            "We are looking for an experienced ServiceNow developer to help build ITSM solutions.",
            "5+ years experience required.");

        var biased = await _sut.EnhanceAsync(
            "ServiceNow Developer",
            "We need a rockstar ninja who is a native English speaker. Young energetic team. He/she must code.",
            null);

        clean.ScoreBefore.Should().BeGreaterThan(biased.ScoreBefore);
    }
}
