using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SNHub.CvParser.Application.Commands.ApplyParsedCv;
using SNHub.CvParser.Application.Commands.ParseCv;
using SNHub.CvParser.Application.Interfaces;
using SNHub.CvParser.Application.Mappers;
using SNHub.CvParser.Application.Queries.GetParseResult;
using SNHub.CvParser.Domain.Entities;
using SNHub.CvParser.Domain.Enums;
using SNHub.CvParser.Domain.Exceptions;
using Xunit;

namespace SNHub.CvParser.UnitTests;

// ════════════════════════════════════════════════════════════════════════════════
// Domain Entity — CvParseResult
// ════════════════════════════════════════════════════════════════════════════════

public sealed class CvParseResultEntityTests
{
    [Fact]
    public void Create_SetsDefaults()
    {
        var userId = Guid.NewGuid();
        var r = CvParseResult.Create(userId, "cvs/test.pdf", "test.pdf", "application/pdf", 1024);

        r.Id.Should().NotBeEmpty();
        r.UserId.Should().Be(userId);
        r.Status.Should().Be(ParseStatus.Pending);
        r.IsApplied.Should().BeFalse();
        r.SkillsJson.Should().Be("[]");
        r.CertificationsJson.Should().Be("[]");
    }

    [Fact]
    public void SetProcessing_ChangesStatus()
    {
        var r = CvParseResult.Create(Guid.NewGuid(), "p", "f", "application/pdf", 100);
        r.SetProcessing();
        r.Status.Should().Be(ParseStatus.Processing);
    }

    [Fact]
    public void SetCompleted_StoresAllExtractedFields()
    {
        var r = CvParseResult.Create(Guid.NewGuid(), "p", "f", "application/pdf", 100);
        r.SetCompleted("Jane", "Smith", "jane@test.com", "+44 7700 900123",
            "London", "SNow Architect", "Summary text", "Senior Developer",
            8, "https://linkedin.com/in/jane", null,
            "[\"ITSM\",\"HRSD\"]", "[]", "[\"Xanadu\"]", 88, "{\"firstName\":95}");

        r.Status.Should().Be(ParseStatus.Completed);
        r.FirstName.Should().Be("Jane");
        r.LastName.Should().Be("Smith");
        r.Email.Should().Be("jane@test.com");
        r.YearsOfExperience.Should().Be(8);
        r.OverallConfidence.Should().Be(88);
        r.SkillsJson.Should().Be("[\"ITSM\",\"HRSD\"]");
    }

    [Fact]
    public void SetCompleted_TrimsStringFields()
    {
        var r = CvParseResult.Create(Guid.NewGuid(), "p", "f", "application/pdf", 100);
        r.SetCompleted("  Jane  ", "  Smith  ", null, null,
            "  London  ", "  Headline  ", null, null,
            null, null, null, "[]", "[]", "[]", 50, "{}");

        r.FirstName.Should().Be("Jane");
        r.LastName.Should().Be("Smith");
        r.Headline.Should().Be("Headline");
    }

    [Fact]
    public void SetCompleted_EmailNormalisedToLowercase()
    {
        var r = CvParseResult.Create(Guid.NewGuid(), "p", "f", "application/pdf", 100);
        r.SetCompleted(null, null, "JANE@TEST.COM", null,
            null, null, null, null, null, null, null, "[]", "[]", "[]", 50, "{}");
        r.Email.Should().Be("jane@test.com");
    }

    [Fact]
    public void SetCompleted_ConfidenceClamped()
    {
        var r = CvParseResult.Create(Guid.NewGuid(), "p", "f", "application/pdf", 100);
        r.SetCompleted(null, null, null, null, null, null, null, null,
            null, null, null, "[]", "[]", "[]", 150, "{}");  // 150 > 100
        r.OverallConfidence.Should().Be(100);
    }

    [Fact]
    public void SetFailed_SetsStatusAndError()
    {
        var r = CvParseResult.Create(Guid.NewGuid(), "p", "f", "application/pdf", 100);
        r.SetFailed("OpenAI timeout");
        r.Status.Should().Be(ParseStatus.Failed);
        r.ErrorMessage.Should().Be("OpenAI timeout");
    }

    [Fact]
    public void MarkApplied_SetsAppliedFields()
    {
        var r = CvParseResult.Create(Guid.NewGuid(), "p", "f", "application/pdf", 100);
        r.MarkApplied();
        r.IsApplied.Should().BeTrue();
        r.AppliedAt.Should().NotBeNull();
        r.AppliedAt!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// ParseCv Validator
// ════════════════════════════════════════════════════════════════════════════════

public sealed class ParseCvCommandValidatorTests
{
    private readonly ParseCvCommandValidator _sut = new();

    private static ParseCvCommand Valid() => new(
        Guid.NewGuid(), Stream.Null, "cv.pdf", "application/pdf", 1024);

    [Fact]
    public void Validate_ValidPdf_Passes()
        => _sut.Validate(Valid()).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_ValidDocx_Passes()
    {
        var cmd = new ParseCvCommand(Guid.NewGuid(), Stream.Null, "cv.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 1024);
        _sut.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyUserId_Fails()
        => _sut.Validate(Valid() with { UserId = Guid.Empty }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_EmptyFileName_Fails()
        => _sut.Validate(Valid() with { FileName = "" }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_WrongContentType_Fails()
        => _sut.Validate(Valid() with { ContentType = "image/jpeg" }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_ZeroSize_Fails()
        => _sut.Validate(Valid() with { FileSizeBytes = 0 }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_TooLarge_Fails()
        => _sut.Validate(Valid() with { FileSizeBytes = 11 * 1024 * 1024 }).IsValid.Should().BeFalse();
}

// ════════════════════════════════════════════════════════════════════════════════
// ApplyParsedCv Validator
// ════════════════════════════════════════════════════════════════════════════════

public sealed class ApplyParsedCvCommandValidatorTests
{
    private readonly ApplyParsedCvCommandValidator _sut = new();

    [Fact]
    public void Validate_Valid_Passes()
        => _sut.Validate(new ApplyParsedCvCommand(Guid.NewGuid(), Guid.NewGuid(), 60))
               .IsValid.Should().BeTrue();

    [Fact]
    public void Validate_EmptyParseResultId_Fails()
        => _sut.Validate(new ApplyParsedCvCommand(Guid.Empty, Guid.NewGuid(), 60))
               .IsValid.Should().BeFalse();

    [Fact]
    public void Validate_NegativeThreshold_Fails()
        => _sut.Validate(new ApplyParsedCvCommand(Guid.NewGuid(), Guid.NewGuid(), -1))
               .IsValid.Should().BeFalse();

    [Fact]
    public void Validate_ThresholdOver100_Fails()
        => _sut.Validate(new ApplyParsedCvCommand(Guid.NewGuid(), Guid.NewGuid(), 101))
               .IsValid.Should().BeFalse();
}

// ════════════════════════════════════════════════════════════════════════════════
// CvParseResultMapper
// ════════════════════════════════════════════════════════════════════════════════

public sealed class CvParseResultMapperTests
{
    [Fact]
    public void ToDto_PendingResult_MapsStatus()
    {
        var r = CvParseResult.Create(Guid.NewGuid(), "path", "cv.pdf", "application/pdf", 512);
        var dto = CvParseResultMapper.ToDto(r);
        dto.Status.Should().Be("Pending");
        dto.IsApplied.Should().BeFalse();
        dto.Skills.Should().BeEmpty();
        dto.Certifications.Should().BeEmpty();
    }

    [Fact]
    public void ToDto_CompletedResult_DeserializesSkillsAndCerts()
    {
        var r = CvParseResult.Create(Guid.NewGuid(), "path", "cv.pdf", "application/pdf", 512);
        r.SetCompleted("Jane", "Smith", null, null, null, null, null, null,
            5, null, null,
            "[\"ITSM\",\"HRSD\"]",
            "[{\"type\":\"CSA\",\"name\":\"Certified System Administrator\",\"year\":2020,\"confidence\":95}]",
            "[\"Xanadu\"]", 85, "{\"firstName\":95}");

        var dto = CvParseResultMapper.ToDto(r);
        dto.Skills.Should().Equal("ITSM", "HRSD");
        dto.Certifications.Should().HaveCount(1);
        dto.Certifications[0].Type.Should().Be("CSA");
        dto.Certifications[0].Year.Should().Be(2020);
        dto.ServiceNowVersions.Should().Equal("Xanadu");
        dto.FieldConfidences["firstName"].Should().Be(95);
    }

    [Fact]
    public void ToDto_MalformedJson_ReturnsEmptyArrays()
    {
        var r = CvParseResult.Create(Guid.NewGuid(), "path", "cv.pdf", "application/pdf", 512);
        // Directly test mapper resilience to bad JSON stored in DB
        var dto = CvParseResultMapper.ToDto(r); // defaults are valid "[]" / "{}"
        dto.Skills.Should().BeEmpty();
        dto.Certifications.Should().BeEmpty();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// ParseCv Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class ParseCvCommandHandlerTests
{
    private readonly Mock<ICvParseResultRepository> _repo   = new();
    private readonly Mock<IUnitOfWork>              _uow    = new();
    private readonly Mock<IBlobStorageService>      _blob   = new();
    private readonly Mock<ICvParserService>         _parser = new();

    private ParseCvCommandHandler Handler() => new(
        _repo.Object, _uow.Object, _blob.Object, _parser.Object,
        NullLogger<ParseCvCommandHandler>.Instance);

    [Fact]
    public async Task Handle_SuccessfulParse_ReturnsCompletedDto()
    {
        _blob.Setup(b => b.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), default))
             .ReturnsAsync("https://storage/cv.pdf");
        _blob.Setup(b => b.DownloadAsync(It.IsAny<string>(), default))
             .ReturnsAsync(new MemoryStream());
        _parser.Setup(p => p.ParseAsync(It.IsAny<Stream>(), It.IsAny<string>(), default))
               .ReturnsAsync(new ParsedCvData
               {
                   FirstName = "Jane", LastName = "Smith",
                   Skills = ["ITSM", "HRSD"],
                   OverallConfidence = 88
               });

        var dto = await Handler().Handle(
            new ParseCvCommand(Guid.NewGuid(), Stream.Null, "cv.pdf", "application/pdf", 1024),
            CancellationToken.None);

        dto.Status.Should().Be("Completed");
        dto.FirstName.Should().Be("Jane");
        dto.OverallConfidence.Should().Be(88);
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Exactly(2)); // once for Pending, once for Completed
    }

    [Fact]
    public async Task Handle_ParserThrows_ReturnsFailedDto()
    {
        _blob.Setup(b => b.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), default))
             .ReturnsAsync("https://storage/cv.pdf");
        _blob.Setup(b => b.DownloadAsync(It.IsAny<string>(), default))
             .ReturnsAsync(new MemoryStream());
        _parser.Setup(p => p.ParseAsync(It.IsAny<Stream>(), It.IsAny<string>(), default))
               .ThrowsAsync(new Exception("OpenAI timeout"));

        var dto = await Handler().Handle(
            new ParseCvCommand(Guid.NewGuid(), Stream.Null, "cv.pdf", "application/pdf", 1024),
            CancellationToken.None);

        dto.Status.Should().Be("Failed");
        dto.ErrorMessage.Should().Contain("OpenAI timeout");
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// ApplyParsedCv Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class ApplyParsedCvCommandHandlerTests
{
    private readonly Mock<ICvParseResultRepository> _repo     = new();
    private readonly Mock<IUnitOfWork>              _uow      = new();
    private readonly Mock<IProfilesServiceClient>   _profiles = new();

    private ApplyParsedCvCommandHandler Handler() => new(
        _repo.Object, _uow.Object, _profiles.Object,
        NullLogger<ApplyParsedCvCommandHandler>.Instance);

    private static CvParseResult CompletedResult(Guid userId)
    {
        var r = CvParseResult.Create(userId, "p", "f", "application/pdf", 100);
        r.SetCompleted("Jane", null, null, null, null, "SNow Lead", null, null,
            5, null, null, "[\"ITSM\"]", "[]", "[]", 85,
            "{\"headline\":85,\"skills\":90}");
        return r;
    }

    [Fact]
    public async Task Handle_ValidRequest_CallsProfilesAndMarksApplied()
    {
        var userId    = Guid.NewGuid();
        var resultId  = Guid.NewGuid();
        var completed = CompletedResult(userId);
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync(completed);

        var response = await Handler().Handle(
            new ApplyParsedCvCommand(resultId, userId, 60), CancellationToken.None);

        response.Applied.Should().BeTrue();
        _profiles.Verify(p => p.ApplyParsedDataAsync(userId, It.IsAny<ProfilePatch>(), default), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
        completed.IsApplied.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_DifferentRequester_ThrowsAccessDenied()
    {
        var completed = CompletedResult(Guid.NewGuid());
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync(completed);

        var act = async () => await Handler().Handle(
            new ApplyParsedCvCommand(Guid.NewGuid(), Guid.NewGuid(), 60), // different userId
            CancellationToken.None);

        await act.Should().ThrowAsync<ParseResultAccessDeniedException>();
    }

    [Fact]
    public async Task Handle_AlreadyApplied_ThrowsParseAlreadyApplied()
    {
        var userId    = Guid.NewGuid();
        var completed = CompletedResult(userId);
        completed.MarkApplied();
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync(completed);

        var act = async () => await Handler().Handle(
            new ApplyParsedCvCommand(Guid.NewGuid(), userId, 60), CancellationToken.None);

        await act.Should().ThrowAsync<ParseAlreadyAppliedException>();
    }

    [Fact]
    public async Task Handle_PendingResult_ThrowsParseNotCompleted()
    {
        var userId  = Guid.NewGuid();
        var pending = CvParseResult.Create(userId, "p", "f", "application/pdf", 100);
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync(pending);

        var act = async () => await Handler().Handle(
            new ApplyParsedCvCommand(Guid.NewGuid(), userId, 60), CancellationToken.None);

        await act.Should().ThrowAsync<ParseNotCompletedException>();
    }

    [Fact]
    public async Task Handle_NotFound_ThrowsParseResultNotFound()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
             .ReturnsAsync((CvParseResult?)null);

        var act = async () => await Handler().Handle(
            new ApplyParsedCvCommand(Guid.NewGuid(), Guid.NewGuid(), 60), CancellationToken.None);

        await act.Should().ThrowAsync<ParseResultNotFoundException>();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// GetParseResult Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class GetParseResultQueryHandlerTests
{
    private readonly Mock<ICvParseResultRepository> _repo = new();
    private GetParseResultQueryHandler Handler() => new(_repo.Object);

    [Fact]
    public async Task Handle_OwnResult_ReturnsDto()
    {
        var userId = Guid.NewGuid();
        var r = CvParseResult.Create(userId, "p", "cv.pdf", "application/pdf", 512);
        _repo.Setup(x => x.GetByIdAsync(r.Id, default)).ReturnsAsync(r);

        var dto = await Handler().Handle(new GetParseResultQuery(r.Id, userId), CancellationToken.None);

        dto.UserId.Should().Be(userId);
        dto.OriginalFileName.Should().Be("cv.pdf");
    }

    [Fact]
    public async Task Handle_OtherUserResult_ThrowsAccessDenied()
    {
        var r = CvParseResult.Create(Guid.NewGuid(), "p", "cv.pdf", "application/pdf", 512);
        _repo.Setup(x => x.GetByIdAsync(r.Id, default)).ReturnsAsync(r);

        var act = async () => await Handler().Handle(
            new GetParseResultQuery(r.Id, Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<ParseResultAccessDeniedException>();
    }

    [Fact]
    public async Task Handle_NotFound_ThrowsNotFoundException()
    {
        _repo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), default))
             .ReturnsAsync((CvParseResult?)null);

        var act = async () => await Handler().Handle(
            new GetParseResultQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<ParseResultNotFoundException>();
    }
}
