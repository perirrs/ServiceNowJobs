using FluentAssertions;
using FluentValidation;
using Moq;
using SNHub.Jobs.Application.Commands.CloseJob;
using SNHub.Jobs.Application.Commands.CreateJob;
using SNHub.Jobs.Application.Commands.PauseJob;
using SNHub.Jobs.Application.Commands.PublishJob;
using SNHub.Jobs.Application.Commands.UpdateJob;
using SNHub.Jobs.Application.DTOs;
using SNHub.Jobs.Application.Interfaces;
using SNHub.Jobs.Application.Queries.GetJob;
using SNHub.Jobs.Application.Queries.GetMyJobs;
using SNHub.Jobs.Application.Queries.SearchJobs;
using SNHub.Jobs.Domain.Entities;
using SNHub.Jobs.Domain.Enums;
using SNHub.Jobs.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace SNHub.Jobs.UnitTests;

// ════════════════════════════════════════════════════════════════════════════════
// Domain Entity Tests
// ════════════════════════════════════════════════════════════════════════════════

public sealed class JobEntityTests
{
    private static Job MakeJob(Guid? employerId = null) => Job.Create(
        employerId ?? Guid.NewGuid(), "ServiceNow ITSM Developer", "Great role for ITSM experts.",
        JobType.FullTime, WorkMode.Remote, ExperienceLevel.MidLevel,
        "London", "GBR", "Acme Corp",
        salaryMin: 50_000, salaryMax: 70_000, currency: "GBP",
        salaryVisible: true, expiresAt: DateTimeOffset.UtcNow.AddDays(30));

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_ValidData_ReturnsJobWithDraftStatus()
    {
        var job = MakeJob();
        job.Status.Should().Be(JobStatus.Draft);
        job.Id.Should().NotBeEmpty();
        job.IsActive.Should().BeFalse("draft jobs are not active");
    }

    [Fact]
    public void Create_TitleIsTrimmed()
    {
        var job = Job.Create(Guid.NewGuid(), "  ITSM Developer  ", "desc",
            JobType.Contract, WorkMode.Remote, ExperienceLevel.Senior,
            null, null, null, null, null, null, false, null);
        job.Title.Should().Be("ITSM Developer");
    }

    // ── Status transitions ────────────────────────────────────────────────────

    [Fact]
    public void Publish_DraftJob_SetsStatusActive()
    {
        var job = MakeJob();
        job.Publish();
        job.Status.Should().Be(JobStatus.Active);
        job.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Pause_ActiveJob_SetsStatusPaused()
    {
        var job = MakeJob();
        job.Publish();
        job.Pause();
        job.Status.Should().Be(JobStatus.Paused);
        job.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Close_AnyJob_SetsStatusClosed()
    {
        var job = MakeJob();
        job.Publish();
        job.Close();
        job.Status.Should().Be(JobStatus.Closed);
        job.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_ExpiredJob_ReturnsFalse()
    {
        var job = Job.Create(Guid.NewGuid(), "Expired Job", "desc",
            JobType.FullTime, WorkMode.OnSite, ExperienceLevel.Junior,
            null, null, null, null, null, null, false,
            expiresAt: DateTimeOffset.UtcNow.AddSeconds(-1));
        job.Publish();
        job.IsActive.Should().BeFalse("expired jobs are not active even if status is Active");
    }

    // ── Skills / Certifications / Versions ───────────────────────────────────

    [Fact]
    public void SetSkills_UpdatesSkillsJsonAndTimestamp()
    {
        var job = MakeJob();
        var before = job.UpdatedAt;
        job.SetSkills("[\"ITSM\",\"HRSD\"]");
        job.SkillsRequired.Should().Be("[\"ITSM\",\"HRSD\"]");
        job.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void SetCertifications_UpdatesCertifications()
    {
        var job = MakeJob();
        job.SetCertifications("[\"CSA\",\"CIS-ITSM\"]");
        job.CertificationsRequired.Should().Be("[\"CSA\",\"CIS-ITSM\"]");
    }

    [Fact]
    public void SetServiceNowVersions_UpdatesVersions()
    {
        var job = MakeJob();
        job.SetServiceNowVersions("[\"Xanadu\",\"Washington\"]");
        job.ServiceNowVersions.Should().Be("[\"Xanadu\",\"Washington\"]");
    }

    // ── Counters ──────────────────────────────────────────────────────────────

    [Fact]
    public void IncrementViews_IncreasesViewCount()
    {
        var job = MakeJob();
        job.IncrementViews();
        job.IncrementViews();
        job.ViewCount.Should().Be(2);
    }

    [Fact]
    public void IncrementApplications_ThenDecrement_CountIsCorrect()
    {
        var job = MakeJob();
        job.IncrementApplications();
        job.IncrementApplications();
        job.DecrementApplications();
        job.ApplicationCount.Should().Be(1);
    }

    [Fact]
    public void DecrementApplications_WhenZero_DoesNotGoNegative()
    {
        var job = MakeJob();
        job.DecrementApplications();
        job.ApplicationCount.Should().Be(0);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public void Update_ChangesFields()
    {
        var job = MakeJob();
        job.Update("Updated Title", "New desc", null, null,
            JobType.Contract, WorkMode.Hybrid, ExperienceLevel.Senior,
            "Manchester", "GBR", 60_000, 80_000, "GBP", true,
            DateTimeOffset.UtcNow.AddDays(60));
        job.Title.Should().Be("Updated Title");
        job.JobType.Should().Be(JobType.Contract);
        job.WorkMode.Should().Be(WorkMode.Hybrid);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// JobMapper Tests
// ════════════════════════════════════════════════════════════════════════════════

public sealed class JobMapperTests
{
    [Fact]
    public void ToDto_MapsAllFields()
    {
        var job = Job.Create(Guid.NewGuid(), "Dev Role", "Description",
            JobType.FullTime, WorkMode.Remote, ExperienceLevel.Senior,
            "London", "GBR", "Tech Co",
            60_000, 80_000, "GBP", true,
            DateTimeOffset.UtcNow.AddDays(30));
        job.SetSkills("[\"ITSM\",\"Flow Designer\"]");
        job.SetCertifications("[\"CSA\"]");

        var dto = JobMapper.ToDto(job);

        dto.Id.Should().Be(job.Id);
        dto.Title.Should().Be("Dev Role");
        dto.JobType.Should().Be("FullTime");
        dto.WorkMode.Should().Be("Remote");
        dto.Status.Should().Be("Draft");
        dto.SkillsRequired.Should().Contain("ITSM").And.Contain("Flow Designer");
        dto.CertificationsRequired.Should().Contain("CSA");
    }

    [Fact]
    public void ToDto_EmptySkillsJson_ReturnsEmptyCollection()
    {
        var job = Job.Create(Guid.NewGuid(), "Title", "Desc",
            JobType.FullTime, WorkMode.Remote, ExperienceLevel.Junior,
            null, null, null, null, null, null, false, null);

        var dto = JobMapper.ToDto(job);
        dto.SkillsRequired.Should().BeEmpty();
        dto.CertificationsRequired.Should().BeEmpty();
        dto.ServiceNowVersions.Should().BeEmpty();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// CreateJob — Command Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class CreateJobCommandHandlerTests
{
    private readonly Mock<IJobRepository> _repo = new();
    private readonly Mock<IUnitOfWork>    _uow  = new();

    private CreateJobCommandHandler Handler() =>
        new(_repo.Object, _uow.Object, NullLogger<CreateJobCommandHandler>.Instance);

    private static CreateJobCommand ValidCommand(Guid? employerId = null) => new(
        EmployerId:              employerId ?? Guid.NewGuid(),
        Title:                   "ServiceNow Developer",
        Description:             "Build and configure ServiceNow modules.",
        Requirements:            "3+ years experience",
        Benefits:                "Remote work, pension",
        CompanyName:             "TechCorp",
        JobType:                 JobType.FullTime,
        WorkMode:                WorkMode.Remote,
        ExperienceLevel:         ExperienceLevel.MidLevel,
        Location:                "London",
        Country:                 "GBR",
        SalaryMin:               50_000,
        SalaryMax:               70_000,
        SalaryCurrency:          "GBP",
        IsSalaryVisible:         true,
        SkillsRequired:          ["ITSM", "Flow Designer"],
        CertificationsRequired:  ["CSA"],
        ServiceNowVersions:      ["Xanadu"],
        PublishImmediately:      false,
        ExpiresAt:               DateTimeOffset.UtcNow.AddDays(30));

    [Fact]
    public async Task Handle_ValidCommand_ReturnsJobDto()
    {
        var dto = await Handler().Handle(ValidCommand(), CancellationToken.None);
        dto.Should().NotBeNull();
        dto.Title.Should().Be("ServiceNow Developer");
        dto.Status.Should().Be("Draft");
    }

    [Fact]
    public async Task Handle_PublishImmediately_ReturnsActiveStatus()
    {
        var dto = await Handler().Handle(ValidCommand() with { PublishImmediately = true }, CancellationToken.None);
        dto.Status.Should().Be("Active");
    }

    [Fact]
    public async Task Handle_SkillsSet_AppearsInDto()
    {
        var dto = await Handler().Handle(ValidCommand(), CancellationToken.None);
        dto.SkillsRequired.Should().Contain("ITSM").And.Contain("Flow Designer");
    }

    [Fact]
    public async Task Handle_PersistsJobAndSaves()
    {
        await Handler().Handle(ValidCommand(), CancellationToken.None);
        _repo.Verify(r => r.AddAsync(It.IsAny<Job>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// CreateJob — Validator
// ════════════════════════════════════════════════════════════════════════════════

public sealed class CreateJobCommandValidatorTests
{
    private readonly CreateJobCommandValidator _sut = new();

    private static CreateJobCommand Valid() => new(
        Guid.NewGuid(), "Title", "Description that is long enough",
        null, null, null,
        JobType.FullTime, WorkMode.Remote, ExperienceLevel.MidLevel,
        null, null, null, null, null, false,
        null, null, null, false, null);

    [Fact]
    public void Validate_ValidCommand_Passes()
        => _sut.Validate(Valid()).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyTitle_Fails(string title)
        => _sut.Validate(Valid() with { Title = title }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_TitleTooLong_Fails()
        => _sut.Validate(Valid() with { Title = new string('x', 201) }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_EmptyDescription_Fails()
        => _sut.Validate(Valid() with { Description = "" }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_SalaryMaxLessThanMin_Fails()
    {
        var result = _sut.Validate(Valid() with { SalaryMin = 80_000, SalaryMax = 50_000 });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("SalaryMax"));
    }

    [Fact]
    public void Validate_SalaryMaxEqualToMin_Passes()
        => _sut.Validate(Valid() with { SalaryMin = 50_000, SalaryMax = 50_000 }).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_InvalidCurrencyCode_Fails()
        => _sut.Validate(Valid() with { SalaryCurrency = "US" }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_ValidCurrencyCode_Passes()
        => _sut.Validate(Valid() with { SalaryCurrency = "USD" }).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_ExpiresAtInPast_Fails()
        => _sut.Validate(Valid() with { ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_ExpiresAtInFuture_Passes()
        => _sut.Validate(Valid() with { ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) }).IsValid.Should().BeTrue();
}

// ════════════════════════════════════════════════════════════════════════════════
// UpdateJob — Command Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class UpdateJobCommandHandlerTests
{
    private readonly Mock<IJobRepository> _repo = new();
    private readonly Mock<IUnitOfWork>    _uow  = new();

    private UpdateJobCommandHandler Handler() =>
        new(_repo.Object, _uow.Object, NullLogger<UpdateJobCommandHandler>.Instance);

    private static Job MakeJob(Guid employerId)
    {
        var job = Job.Create(employerId, "Old Title", "Old Desc",
            JobType.FullTime, WorkMode.Remote, ExperienceLevel.MidLevel,
            "London", "GBR", "Corp", 50_000, 70_000, "GBP", true, null);
        job.Publish();
        return job;
    }

    [Fact]
    public async Task Handle_ValidRequest_UpdatesAndReturnsDto()
    {
        var employerId = Guid.NewGuid();
        var job = MakeJob(employerId);
        _repo.Setup(r => r.GetByIdAsync(job.Id, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        var dto = await Handler().Handle(new UpdateJobCommand(
            job.Id, employerId, "New Title", "New Description", null, null,
            JobType.Contract, WorkMode.Hybrid, ExperienceLevel.Senior,
            "Manchester", "GBR", 60_000, 80_000, "GBP", true,
            null, null, null,
            DateTimeOffset.UtcNow.AddDays(60)), CancellationToken.None);

        dto.Title.Should().Be("New Title");
        dto.JobType.Should().Be("Contract");
    }

    [Fact]
    public async Task Handle_WrongRequester_ThrowsJobAccessDeniedException()
    {
        var job = MakeJob(Guid.NewGuid());
        _repo.Setup(r => r.GetByIdAsync(job.Id, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        var act = async () => await Handler().Handle(new UpdateJobCommand(
            job.Id, Guid.NewGuid(), "Title", "Desc", null, null,
            JobType.FullTime, WorkMode.Remote, ExperienceLevel.Junior,
            null, null, null, null, null, false, null, null, null, null), CancellationToken.None);

        await act.Should().ThrowAsync<JobAccessDeniedException>();
    }

    [Fact]
    public async Task Handle_JobNotFound_ThrowsJobNotFoundException()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Job?)null);

        var act = async () => await Handler().Handle(new UpdateJobCommand(
            Guid.NewGuid(), Guid.NewGuid(), "T", "D", null, null,
            JobType.FullTime, WorkMode.Remote, ExperienceLevel.Junior,
            null, null, null, null, null, false, null, null, null, null), CancellationToken.None);

        await act.Should().ThrowAsync<JobNotFoundException>();
    }

    [Fact]
    public async Task Handle_ClosedJob_ThrowsJobNotActiveException()
    {
        var employerId = Guid.NewGuid();
        var job = MakeJob(employerId);
        job.Close();
        _repo.Setup(r => r.GetByIdAsync(job.Id, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        var act = async () => await Handler().Handle(new UpdateJobCommand(
            job.Id, employerId, "T", "D", null, null,
            JobType.FullTime, WorkMode.Remote, ExperienceLevel.Junior,
            null, null, null, null, null, false, null, null, null, null), CancellationToken.None);

        await act.Should().ThrowAsync<JobNotActiveException>();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// PublishJob — Command Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class PublishJobCommandHandlerTests
{
    private readonly Mock<IJobRepository> _repo = new();
    private readonly Mock<IUnitOfWork>    _uow  = new();

    private PublishJobCommandHandler Handler() =>
        new(_repo.Object, _uow.Object, NullLogger<PublishJobCommandHandler>.Instance);

    [Fact]
    public async Task Handle_DraftJob_PublishesAndReturnsActiveStatus()
    {
        var employerId = Guid.NewGuid();
        var job = Job.Create(employerId, "Title", "Desc",
            JobType.FullTime, WorkMode.Remote, ExperienceLevel.Senior,
            null, null, null, null, null, null, false, null);
        _repo.Setup(r => r.GetByIdAsync(job.Id, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        var dto = await Handler().Handle(new PublishJobCommand(job.Id, employerId), CancellationToken.None);
        dto.Status.Should().Be("Active");
    }

    [Fact]
    public async Task Handle_AlreadyActiveJob_ThrowsDomainException()
    {
        var employerId = Guid.NewGuid();
        var job = Job.Create(employerId, "Title", "Desc",
            JobType.FullTime, WorkMode.Remote, ExperienceLevel.Senior,
            null, null, null, null, null, null, false, null);
        job.Publish();
        _repo.Setup(r => r.GetByIdAsync(job.Id, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        var act = async () => await Handler().Handle(new PublishJobCommand(job.Id, employerId), CancellationToken.None);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*already published*");
    }

    [Fact]
    public async Task Handle_ClosedJob_ThrowsJobNotActiveException()
    {
        var employerId = Guid.NewGuid();
        var job = Job.Create(employerId, "Title", "Desc",
            JobType.FullTime, WorkMode.Remote, ExperienceLevel.Senior,
            null, null, null, null, null, null, false, null);
        job.Publish();
        job.Close();
        _repo.Setup(r => r.GetByIdAsync(job.Id, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        var act = async () => await Handler().Handle(new PublishJobCommand(job.Id, employerId), CancellationToken.None);
        await act.Should().ThrowAsync<JobNotActiveException>();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// PauseJob — Command Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class PauseJobCommandHandlerTests
{
    private readonly Mock<IJobRepository> _repo = new();
    private readonly Mock<IUnitOfWork>    _uow  = new();

    private PauseJobCommandHandler Handler() =>
        new(_repo.Object, _uow.Object, NullLogger<PauseJobCommandHandler>.Instance);

    [Fact]
    public async Task Handle_ActiveJob_PausesAndReturnsPausedStatus()
    {
        var employerId = Guid.NewGuid();
        var job = Job.Create(employerId, "Title", "Desc",
            JobType.FullTime, WorkMode.Remote, ExperienceLevel.MidLevel,
            null, null, null, null, null, null, false, null);
        job.Publish();
        _repo.Setup(r => r.GetByIdAsync(job.Id, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        var dto = await Handler().Handle(new PauseJobCommand(job.Id, employerId), CancellationToken.None);
        dto.Status.Should().Be("Paused");
    }

    [Fact]
    public async Task Handle_DraftJob_ThrowsDomainException()
    {
        var employerId = Guid.NewGuid();
        var job = Job.Create(employerId, "Title", "Desc",
            JobType.FullTime, WorkMode.Remote, ExperienceLevel.MidLevel,
            null, null, null, null, null, null, false, null);
        _repo.Setup(r => r.GetByIdAsync(job.Id, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        var act = async () => await Handler().Handle(new PauseJobCommand(job.Id, employerId), CancellationToken.None);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*Only active jobs*");
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// CloseJob — Command Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class CloseJobCommandHandlerTests
{
    private readonly Mock<IJobRepository> _repo = new();
    private readonly Mock<IUnitOfWork>    _uow  = new();

    private CloseJobCommandHandler Handler() =>
        new(_repo.Object, _uow.Object, NullLogger<CloseJobCommandHandler>.Instance);

    [Fact]
    public async Task Handle_ValidRequest_ClosesJob()
    {
        var employerId = Guid.NewGuid();
        var job = Job.Create(employerId, "Title", "Desc",
            JobType.FullTime, WorkMode.Remote, ExperienceLevel.Senior,
            null, null, null, null, null, null, false, null);
        job.Publish();
        _repo.Setup(r => r.GetByIdAsync(job.Id, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        await Handler().Handle(new CloseJobCommand(job.Id, employerId), CancellationToken.None);

        job.Status.Should().Be(JobStatus.Closed);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WrongEmployer_ThrowsJobAccessDeniedException()
    {
        var job = Job.Create(Guid.NewGuid(), "Title", "Desc",
            JobType.FullTime, WorkMode.Remote, ExperienceLevel.Senior,
            null, null, null, null, null, null, false, null);
        _repo.Setup(r => r.GetByIdAsync(job.Id, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        var act = async () => await Handler().Handle(
            new CloseJobCommand(job.Id, Guid.NewGuid()), CancellationToken.None);
        await act.Should().ThrowAsync<JobAccessDeniedException>();
    }

    [Fact]
    public async Task Handle_JobNotFound_ThrowsJobNotFoundException()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Job?)null);

        var act = async () => await Handler().Handle(
            new CloseJobCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);
        await act.Should().ThrowAsync<JobNotFoundException>();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// GetJob — Query Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class GetJobQueryHandlerTests
{
    private readonly Mock<IJobRepository> _repo = new();
    private readonly Mock<IUnitOfWork>    _uow  = new();
    private GetJobQueryHandler Handler() => new(_repo.Object, _uow.Object);

    [Fact]
    public async Task Handle_ExistingJob_IncrementsViews()
    {
        var job = Job.Create(Guid.NewGuid(), "Title", "Desc",
            JobType.FullTime, WorkMode.Remote, ExperienceLevel.MidLevel,
            null, null, null, null, null, null, false, null);
        _repo.Setup(r => r.GetByIdAsync(job.Id, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        await Handler().Handle(new GetJobQuery(job.Id), CancellationToken.None);

        job.ViewCount.Should().Be(1);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NonExistentJob_ThrowsJobNotFoundException()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Job?)null);

        var act = async () => await Handler().Handle(new GetJobQuery(Guid.NewGuid()), CancellationToken.None);
        await act.Should().ThrowAsync<JobNotFoundException>();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// SearchJobs — Query Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class SearchJobsQueryHandlerTests
{
    private readonly Mock<IJobRepository> _repo = new();
    private SearchJobsQueryHandler Handler() => new(_repo.Object);

    [Fact]
    public async Task Handle_NoFilters_ReturnsPagedResult()
    {
        var jobs = new List<Job>
        {
            Job.Create(Guid.NewGuid(), "Job 1", "Desc", JobType.FullTime, WorkMode.Remote,
                ExperienceLevel.MidLevel, null, null, null, null, null, null, false, null),
            Job.Create(Guid.NewGuid(), "Job 2", "Desc", JobType.Contract, WorkMode.Hybrid,
                ExperienceLevel.Senior, null, null, null, null, null, null, false, null),
        };
        _repo.Setup(r => r.SearchAsync(
                null, null, null, null, null, null, null, null, JobStatus.Active, null, 1, 20, default))
             .ReturnsAsync((jobs, 2));

        var result = await Handler().Handle(
            new SearchJobsQuery(null, null, null, null, null, null, null, null), CancellationToken.None);

        result.Total.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.TotalPages.Should().Be(1);
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_EmptyResults_ReturnsZeroTotalPages()
    {
        _repo.Setup(r => r.SearchAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<JobType?>(), It.IsAny<WorkMode?>(), It.IsAny<ExperienceLevel?>(),
                It.IsAny<decimal?>(), It.IsAny<decimal?>(), It.IsAny<JobStatus?>(),
                It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((new List<Job>(), 0));

        var result = await Handler().Handle(
            new SearchJobsQuery("no results", null, null, null, null, null, null, null),
            CancellationToken.None);

        result.Total.Should().Be(0);
        result.TotalPages.Should().Be(0);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// GetMyJobs — Query Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class GetMyJobsQueryHandlerTests
{
    private readonly Mock<IJobRepository> _repo = new();
    private GetMyJobsQueryHandler Handler() => new(_repo.Object);

    [Fact]
    public async Task Handle_WithStatusFilter_PassesStatusToRepo()
    {
        var employerId = Guid.NewGuid();
        _repo.Setup(r => r.SearchAsync(
                null, null, null, null, null, null, null, null,
                JobStatus.Draft, employerId, 1, 20, default))
             .ReturnsAsync((new List<Job>(), 0));

        var result = await Handler().Handle(
            new GetMyJobsQuery(employerId, JobStatus.Draft), CancellationToken.None);

        result.Total.Should().Be(0);
        _repo.Verify(r => r.SearchAsync(
            null, null, null, null, null, null, null, null,
            JobStatus.Draft, employerId, 1, 20, default), Times.Once);
    }

    [Fact]
    public async Task Handle_NoStatusFilter_PassesNullStatusToRepo()
    {
        var employerId = Guid.NewGuid();
        _repo.Setup(r => r.SearchAsync(
                null, null, null, null, null, null, null, null,
                null, employerId, 1, 20, default))
             .ReturnsAsync((new List<Job>(), 0));

        await Handler().Handle(new GetMyJobsQuery(employerId, Status: null), CancellationToken.None);

        _repo.Verify(r => r.SearchAsync(
            null, null, null, null, null, null, null, null,
            null, employerId, 1, 20, default), Times.Once);
    }
}
