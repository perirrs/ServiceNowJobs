using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SNHub.Profiles.Application.Commands.UploadFile;
using SNHub.Profiles.Application.Commands.UpsertCandidateProfile;
using SNHub.Profiles.Application.Commands.UpsertEmployerProfile;
using SNHub.Profiles.Application.DTOs;
using SNHub.Profiles.Application.Interfaces;
using SNHub.Profiles.Application.Queries.GetProfile;
using SNHub.Profiles.Domain.Entities;
using SNHub.Profiles.Domain.Enums;
using SNHub.Profiles.Domain.Exceptions;
using Xunit;

namespace SNHub.Profiles.UnitTests;

// ════════════════════════════════════════════════════════════════════════════════
// Domain Entity — CandidateProfile
// ════════════════════════════════════════════════════════════════════════════════

public sealed class CandidateProfileEntityTests
{
    [Fact]
    public void Create_ValidUserId_SetsDefaults()
    {
        var userId  = Guid.NewGuid();
        var profile = CandidateProfile.Create(userId);

        profile.UserId.Should().Be(userId);
        profile.Id.Should().NotBeEmpty();
        profile.IsPublic.Should().BeTrue();
        profile.SalaryCurrency.Should().Be("USD");
        profile.Availability.Should().Be(AvailabilityStatus.OpenToOpportunities);
        profile.ProfileCompleteness.Should().Be(0);
    }

    [Fact]
    public void Update_AllFields_Persisted()
    {
        var profile = CandidateProfile.Create(Guid.NewGuid());
        profile.Update("Senior SN Dev", "Experienced ServiceNow architect.",
            ExperienceLevel.Senior, 8, AvailabilityStatus.ActivelyLooking,
            "Principal", "Architect", "London", "GBR", "Europe/London",
            "https://linkedin.com/in/test", "https://github.com/test", "https://test.dev",
            true, 80_000m, 120_000m, "GBP", true, false);

        profile.Headline.Should().Be("Senior SN Dev");
        profile.ExperienceLevel.Should().Be(ExperienceLevel.Senior);
        profile.YearsOfExperience.Should().Be(8);
        profile.OpenToRemote.Should().BeTrue();
        profile.SalaryCurrency.Should().Be("GBP");
    }

    [Fact]
    public void Update_TrimsWhitespace()
    {
        var profile = CandidateProfile.Create(Guid.NewGuid());
        profile.Update("  Senior Dev  ", "  Bio  ", ExperienceLevel.Mid, 5,
            AvailabilityStatus.OpenToOpportunities,
            null, null, null, null, null, null, null, null,
            true, null, null, null, false, false);

        profile.Headline.Should().Be("Senior Dev");
        profile.Bio.Should().Be("Bio");
    }

    [Fact]
    public void SetSkills_UpdatesJson()
    {
        var profile = CandidateProfile.Create(Guid.NewGuid());
        profile.SetSkills("[\"ITSM\",\"HRSD\"]");
        profile.SkillsJson.Should().Be("[\"ITSM\",\"HRSD\"]");
    }

    [Fact]
    public void SetCertifications_UpdatesJson()
    {
        var profile = CandidateProfile.Create(Guid.NewGuid());
        profile.SetCertifications("[{\"type\":1,\"name\":\"CSA\"}]");
        profile.CertificationsJson.Should().NotBe("[]");
    }

    [Fact]
    public void RecalculateCompleteness_FullProfile_Reaches100()
    {
        var profile = CandidateProfile.Create(Guid.NewGuid());
        profile.Update("Great headline", "A thorough bio of this candidate.",
            ExperienceLevel.Senior, 10, AvailabilityStatus.ActivelyLooking,
            null, null, "London", null, null,
            "https://linkedin.com", null, null,
            true, null, null, null, false, false);
        profile.SetSkills("[\"ITSM\"]");
        profile.SetCertifications("[{\"name\":\"CSA\"}]");
        profile.SetProfilePicture("https://cdn.example.com/pic.jpg");
        profile.SetCvUrl("https://cdn.example.com/cv.pdf");

        profile.ProfileCompleteness.Should().Be(100);
    }

    [Fact]
    public void RecalculateCompleteness_EmptyProfile_IsZero()
    {
        var profile = CandidateProfile.Create(Guid.NewGuid());
        profile.ProfileCompleteness.Should().Be(0);
    }

    [Fact]
    public void SetProfilePicture_UpdatesUrl()
    {
        var profile = CandidateProfile.Create(Guid.NewGuid());
        profile.SetProfilePicture("https://cdn.example.com/pic.jpg");
        profile.ProfilePictureUrl.Should().Be("https://cdn.example.com/pic.jpg");
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Domain Entity — EmployerProfile
// ════════════════════════════════════════════════════════════════════════════════

public sealed class EmployerProfileEntityTests
{
    [Fact]
    public void Create_ValidUserId_IsNotVerified()
    {
        var profile = EmployerProfile.Create(Guid.NewGuid());
        profile.IsVerified.Should().BeFalse();
        profile.CompanyName.Should().BeNull();
    }

    [Fact]
    public void Update_Fields_Persisted()
    {
        var profile = EmployerProfile.Create(Guid.NewGuid());
        profile.Update("Acme Corp", "ServiceNow consulting firm", "Technology",
            "51-200", "London", "GBR", "https://acme.com", "https://linkedin.com/company/acme");

        profile.CompanyName.Should().Be("Acme Corp");
        profile.CompanySize.Should().Be("51-200");
        profile.IsVerified.Should().BeFalse();
    }

    [Fact]
    public void Verify_SetsIsVerifiedTrue()
    {
        var profile = EmployerProfile.Create(Guid.NewGuid());
        profile.Verify();
        profile.IsVerified.Should().BeTrue();
    }

    [Fact]
    public void SetLogo_UpdatesLogoUrl()
    {
        var profile = EmployerProfile.Create(Guid.NewGuid());
        profile.SetLogo("https://cdn.example.com/logo.png");
        profile.LogoUrl.Should().Be("https://cdn.example.com/logo.png");
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// ProfileMapper
// ════════════════════════════════════════════════════════════════════════════════

public sealed class ProfileMapperTests
{
    [Fact]
    public void ToDto_CandidateProfile_MapsAllFields()
    {
        var profile = CandidateProfile.Create(Guid.NewGuid());
        profile.Update("Senior Dev", "Bio here.", ExperienceLevel.Senior, 8,
            AvailabilityStatus.ActivelyLooking, "Consultant", "Architect",
            "London", "GBR", "Europe/London",
            "https://linkedin.com", null, null,
            true, 80_000m, 120_000m, "GBP", true, false);
        profile.SetSkills("[\"ITSM\",\"HRSD\"]");

        var dto = ProfileMapper.ToDto(profile);

        dto.Headline.Should().Be("Senior Dev");
        dto.ExperienceLevel.Should().Be("Senior");
        dto.Availability.Should().Be("ActivelyLooking");
        dto.Skills.Should().Contain("ITSM").And.Contain("HRSD");
        dto.SalaryCurrency.Should().Be("GBP");
        dto.OpenToRemote.Should().BeTrue();
    }

    [Fact]
    public void ToDto_EmployerProfile_MapsAllFields()
    {
        var profile = EmployerProfile.Create(Guid.NewGuid());
        profile.Update("Acme Corp", "Description", "Technology", "51-200",
            "London", "GBR", "https://acme.com", null);
        profile.Verify();

        var dto = ProfileMapper.ToDto(profile);

        dto.CompanyName.Should().Be("Acme Corp");
        dto.IsVerified.Should().BeTrue();
    }

    [Fact]
    public void ToDto_InvalidSkillsJson_ReturnsEmptyCollection()
    {
        var profile = CandidateProfile.Create(Guid.NewGuid());
        profile.SetSkills("not-valid-json");

        var dto = ProfileMapper.ToDto(profile);
        dto.Skills.Should().BeEmpty();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// UpsertCandidateProfile — Validator
// ════════════════════════════════════════════════════════════════════════════════

public sealed class UpsertCandidateProfileCommandValidatorTests
{
    private readonly UpsertCandidateProfileCommandValidator _sut = new();

    private static UpsertCandidateProfileCommand Valid() => new(
        Guid.NewGuid(), "ServiceNow Developer", "Great professional.",
        ExperienceLevel.Mid, 5, AvailabilityStatus.OpenToOpportunities,
        "Developer", "Senior Dev", "London", "GBR", "Europe/London",
        "https://linkedin.com/in/test", null, null,
        true, 60_000m, 90_000m, "GBP", true, false,
        ["ITSM", "HRSD"], null, ["Xanadu", "Washington"]);

    [Fact]
    public void Validate_ValidCommand_Passes()
        => _sut.Validate(Valid()).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_EmptyUserId_Fails()
        => _sut.Validate(Valid() with { UserId = Guid.Empty }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_HeadlineTooLong_Fails()
        => _sut.Validate(Valid() with { Headline = new string('x', 201) }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_BiooTooLong_Fails()
        => _sut.Validate(Valid() with { Bio = new string('x', 3_001) }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_NegativeYears_Fails()
        => _sut.Validate(Valid() with { YearsOfExperience = -1 }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_SalaryMaxLessThanMin_Fails()
        => _sut.Validate(Valid() with { DesiredSalaryMin = 100_000m, DesiredSalaryMax = 50_000m }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_InvalidCurrency_Fails()
        => _sut.Validate(Valid() with { SalaryCurrency = "XYZ" }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_TooManySkills_Fails()
    {
        var tooMany = Enumerable.Range(1, 31).Select(i => $"Skill{i}").ToList();
        _sut.Validate(Valid() with { Skills = tooMany }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_InvalidLinkedInUrl_Fails()
        => _sut.Validate(Valid() with { LinkedInUrl = "not-a-url" }).IsValid.Should().BeFalse();
}

// ════════════════════════════════════════════════════════════════════════════════
// UpsertEmployerProfile — Validator
// ════════════════════════════════════════════════════════════════════════════════

public sealed class UpsertEmployerProfileCommandValidatorTests
{
    private readonly UpsertEmployerProfileCommandValidator _sut = new();

    private static UpsertEmployerProfileCommand Valid() => new(
        Guid.NewGuid(), "Acme Corp", "ServiceNow consulting firm.",
        "Technology", "51-200", "London", "GBR",
        "https://acme.com", "https://linkedin.com/company/acme");

    [Fact]
    public void Validate_ValidCommand_Passes()
        => _sut.Validate(Valid()).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_InvalidCompanySize_Fails()
        => _sut.Validate(Valid() with { CompanySize = "999" }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_NullCompanySize_Passes()
        => _sut.Validate(Valid() with { CompanySize = null }).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_InvalidWebsiteUrl_Fails()
        => _sut.Validate(Valid() with { WebsiteUrl = "not-a-url" }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_CompanyNameTooLong_Fails()
        => _sut.Validate(Valid() with { CompanyName = new string('x', 201) }).IsValid.Should().BeFalse();
}

// ════════════════════════════════════════════════════════════════════════════════
// UpsertCandidateProfile — Command Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class UpsertCandidateProfileCommandHandlerTests
{
    private readonly Mock<ICandidateProfileRepository> _repo = new();
    private readonly Mock<IUnitOfWork>                 _uow  = new();

    private UpsertCandidateProfileCommandHandler Handler() =>
        new(_repo.Object, _uow.Object, NullLogger<UpsertCandidateProfileCommandHandler>.Instance);

    private static UpsertCandidateProfileCommand ValidCommand(Guid userId) => new(
        userId, "Dev", "Bio.", ExperienceLevel.Mid, 5,
        AvailabilityStatus.OpenToOpportunities,
        null, null, null, "GBR", null, null, null, null,
        true, null, null, "USD", false, false,
        ["ITSM"], null, null);

    [Fact]
    public async Task Handle_NewProfile_CreatesAndSaves()
    {
        var userId = Guid.NewGuid();
        _repo.Setup(r => r.GetByUserIdAsync(userId, default)).ReturnsAsync((CandidateProfile?)null);

        var dto = await Handler().Handle(ValidCommand(userId), CancellationToken.None);

        dto.UserId.Should().Be(userId);
        dto.Headline.Should().Be("Dev");
        _repo.Verify(r => r.AddAsync(It.IsAny<CandidateProfile>(), default), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_ExistingProfile_UpdatesWithoutAddAsync()
    {
        var userId  = Guid.NewGuid();
        var existing = CandidateProfile.Create(userId);
        _repo.Setup(r => r.GetByUserIdAsync(userId, default)).ReturnsAsync(existing);

        var dto = await Handler().Handle(ValidCommand(userId), CancellationToken.None);

        dto.Headline.Should().Be("Dev");
        _repo.Verify(r => r.AddAsync(It.IsAny<CandidateProfile>(), default), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_WithSkills_SetsSkillsJson()
    {
        var userId = Guid.NewGuid();
        _repo.Setup(r => r.GetByUserIdAsync(userId, default)).ReturnsAsync((CandidateProfile?)null);
        var cmd = ValidCommand(userId) with { Skills = ["ITSM", "HRSD", "CSM"] };

        var dto = await Handler().Handle(cmd, CancellationToken.None);
        dto.Skills.Should().Contain("ITSM").And.Contain("HRSD").And.Contain("CSM");
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// UpsertEmployerProfile — Command Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class UpsertEmployerProfileCommandHandlerTests
{
    private readonly Mock<IEmployerProfileRepository> _repo = new();
    private readonly Mock<IUnitOfWork>                _uow  = new();

    private UpsertEmployerProfileCommandHandler Handler() =>
        new(_repo.Object, _uow.Object, NullLogger<UpsertEmployerProfileCommandHandler>.Instance);

    [Fact]
    public async Task Handle_NewProfile_CreatesAndSaves()
    {
        var userId = Guid.NewGuid();
        _repo.Setup(r => r.GetByUserIdAsync(userId, default)).ReturnsAsync((EmployerProfile?)null);

        var dto = await Handler().Handle(
            new UpsertEmployerProfileCommand(userId, "Acme", "Desc", "Tech",
                "51-200", "London", "GBR", "https://acme.com", null),
            CancellationToken.None);

        dto.CompanyName.Should().Be("Acme");
        _repo.Verify(r => r.AddAsync(It.IsAny<EmployerProfile>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_ExistingProfile_UpdatesOnly()
    {
        var userId   = Guid.NewGuid();
        var existing = EmployerProfile.Create(userId);
        _repo.Setup(r => r.GetByUserIdAsync(userId, default)).ReturnsAsync(existing);

        var dto = await Handler().Handle(
            new UpsertEmployerProfileCommand(userId, "Updated Corp", null, null,
                null, null, null, null, null),
            CancellationToken.None);

        dto.CompanyName.Should().Be("Updated Corp");
        _repo.Verify(r => r.AddAsync(It.IsAny<EmployerProfile>(), default), Times.Never);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// UploadProfilePicture — Command Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class UploadProfilePictureCommandHandlerTests
{
    private readonly Mock<ICandidateProfileRepository> _repo    = new();
    private readonly Mock<IFileStorageService>         _storage = new();
    private readonly Mock<IUnitOfWork>                 _uow     = new();

    private UploadProfilePictureCommandHandler Handler() =>
        new(_repo.Object, _storage.Object, _uow.Object,
            NullLogger<UploadProfilePictureCommandHandler>.Instance);

    [Fact]
    public async Task Handle_ValidJpeg_UploadsAndReturnsUrl()
    {
        var userId = Guid.NewGuid();
        var content = new MemoryStream(new byte[100]);
        _storage.Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), "image/jpeg", default))
                .ReturnsAsync("https://cdn.example.com/pic.jpg");
        _repo.Setup(r => r.GetByUserIdAsync(userId, default)).ReturnsAsync(CandidateProfile.Create(userId));

        var url = await Handler().Handle(
            new UploadProfilePictureCommand(userId, content, "photo.jpg", "image/jpeg", 100),
            CancellationToken.None);

        url.Should().Be("https://cdn.example.com/pic.jpg");
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_InvalidContentType_ThrowsInvalidFileTypeException()
    {
        var content = new MemoryStream(new byte[100]);
        var act = async () => await Handler().Handle(
            new UploadProfilePictureCommand(Guid.NewGuid(), content, "doc.pdf", "application/pdf", 100),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidFileTypeException>().WithMessage("*JPEG*PNG*WebP*");
    }

    [Fact]
    public async Task Handle_FileTooLarge_ThrowsFileTooLargeException()
    {
        var content = new MemoryStream(new byte[100]);
        var act = async () => await Handler().Handle(
            new UploadProfilePictureCommand(Guid.NewGuid(), content, "huge.jpg", "image/jpeg", 6 * 1024 * 1024),
            CancellationToken.None);

        await act.Should().ThrowAsync<FileTooLargeException>().WithMessage("*5MB*");
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// UploadCv — Command Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class UploadCvCommandHandlerTests
{
    private readonly Mock<ICandidateProfileRepository> _repo    = new();
    private readonly Mock<IFileStorageService>         _storage = new();
    private readonly Mock<IUnitOfWork>                 _uow     = new();

    private UploadCvCommandHandler Handler() =>
        new(_repo.Object, _storage.Object, _uow.Object,
            NullLogger<UploadCvCommandHandler>.Instance);

    [Fact]
    public async Task Handle_ValidPdf_UploadsAndReturnsUrl()
    {
        var userId  = Guid.NewGuid();
        var content = new MemoryStream(new byte[100]);
        _storage.Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), "application/pdf", default))
                .ReturnsAsync("https://cdn.example.com/cv.pdf");
        _repo.Setup(r => r.GetByUserIdAsync(userId, default)).ReturnsAsync(CandidateProfile.Create(userId));

        var url = await Handler().Handle(
            new UploadCvCommand(userId, content, "cv.pdf", "application/pdf", 500_000),
            CancellationToken.None);

        url.Should().Be("https://cdn.example.com/cv.pdf");
    }

    [Fact]
    public async Task Handle_NonPdf_ThrowsInvalidFileTypeException()
    {
        var content = new MemoryStream();
        var act = async () => await Handler().Handle(
            new UploadCvCommand(Guid.NewGuid(), content, "cv.docx",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 100),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidFileTypeException>().WithMessage("*PDF*");
    }

    [Fact]
    public async Task Handle_CvTooLarge_ThrowsFileTooLargeException()
    {
        var content = new MemoryStream();
        var act = async () => await Handler().Handle(
            new UploadCvCommand(Guid.NewGuid(), content, "cv.pdf", "application/pdf", 11 * 1024 * 1024),
            CancellationToken.None);

        await act.Should().ThrowAsync<FileTooLargeException>().WithMessage("*10MB*");
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// GetCandidateProfile — Query Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class GetCandidateProfileQueryHandlerTests
{
    private readonly Mock<ICandidateProfileRepository> _repo = new();
    private GetCandidateProfileQueryHandler Handler() => new(_repo.Object);

    [Fact]
    public async Task Handle_ExistingProfile_ReturnsDto()
    {
        var userId  = Guid.NewGuid();
        var profile = CandidateProfile.Create(userId);
        _repo.Setup(r => r.GetByUserIdAsync(userId, default)).ReturnsAsync(profile);

        var dto = await Handler().Handle(new GetCandidateProfileQuery(userId), CancellationToken.None);

        dto.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task Handle_NotFound_ThrowsProfileNotFoundException()
    {
        _repo.Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), default))
             .ReturnsAsync((CandidateProfile?)null);

        var act = async () => await Handler().Handle(
            new GetCandidateProfileQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<ProfileNotFoundException>();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// SearchCandidates — Query Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class SearchCandidatesQueryHandlerTests
{
    private readonly Mock<ICandidateProfileRepository> _repo = new();
    private SearchCandidatesQueryHandler Handler() => new(_repo.Object);

    [Fact]
    public async Task Handle_ReturnsPagedResult()
    {
        var profiles = new List<CandidateProfile>
        {
            CandidateProfile.Create(Guid.NewGuid()),
            CandidateProfile.Create(Guid.NewGuid()),
        };
        _repo.Setup(r => r.SearchAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ExperienceLevel?>(),
                It.IsAny<int?>(), It.IsAny<bool?>(), It.IsAny<AvailabilityStatus?>(),
                1, 20, default))
             .ReturnsAsync((profiles, 2));

        var result = await Handler().Handle(
            new SearchCandidatesQuery(null, null, null, null, null, null), CancellationToken.None);

        result.Total.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.HasNextPage.Should().BeFalse();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// PagedResult factory
// ════════════════════════════════════════════════════════════════════════════════

public sealed class PagedResultTests
{
    [Theory]
    [InlineData(0,  10, 1, 0, false, false)]
    [InlineData(10, 10, 1, 1, false, false)]
    [InlineData(25, 10, 1, 3, true,  false)]
    [InlineData(25, 10, 2, 3, true,  true)]
    [InlineData(25, 10, 3, 3, false, true)]
    public void Create_ComputesPaginationCorrectly(
        int total, int pageSize, int page, int expectedPages,
        bool expectNext, bool expectPrev)
    {
        var result = PagedResult<int>.Create([], total, page, pageSize);
        result.TotalPages.Should().Be(expectedPages);
        result.HasNextPage.Should().Be(expectNext);
        result.HasPreviousPage.Should().Be(expectPrev);
    }
}
