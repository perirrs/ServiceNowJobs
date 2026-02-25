using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SNHub.Applications.Application.Commands.ApplyToJob;
using SNHub.Applications.Application.Commands.UpdateStatus;
using SNHub.Applications.Application.Commands.Withdraw;
using SNHub.Applications.Application.DTOs;
using SNHub.Applications.Application.Interfaces;
using SNHub.Applications.Application.Queries.GetApplications;
using SNHub.Applications.Domain.Entities;
using SNHub.Applications.Domain.Enums;
using SNHub.Applications.Domain.Exceptions;
using Xunit;

namespace SNHub.Applications.UnitTests;

// ════════════════════════════════════════════════════════════════════════════════
// Domain Entity — JobApplication
// ════════════════════════════════════════════════════════════════════════════════

public sealed class JobApplicationEntityTests
{
    private static JobApplication MakeApplication() =>
        JobApplication.Create(Guid.NewGuid(), Guid.NewGuid(), "Great role!", "https://cv.example.com/cv.pdf");

    [Fact]
    public void Create_ValidData_ReturnsApplicationWithAppliedStatus()
    {
        var app = MakeApplication();
        app.Status.Should().Be(ApplicationStatus.Applied);
        app.IsActive.Should().BeTrue();
        app.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_CoverLetterIsTrimmed()
    {
        var app = JobApplication.Create(Guid.NewGuid(), Guid.NewGuid(), "  Great role!  ", null);
        app.CoverLetter.Should().Be("Great role!");
    }

    [Fact]
    public void Create_NullCoverLetter_IsAllowed()
    {
        var app = JobApplication.Create(Guid.NewGuid(), Guid.NewGuid(), null, null);
        app.CoverLetter.Should().BeNull();
        app.IsActive.Should().BeTrue();
    }

    // ── Status transitions ────────────────────────────────────────────────────

    [Theory]
    [InlineData(ApplicationStatus.Screening)]
    [InlineData(ApplicationStatus.Interview)]
    [InlineData(ApplicationStatus.Offer)]
    [InlineData(ApplicationStatus.Hired)]
    [InlineData(ApplicationStatus.Rejected)]
    public void UpdateStatus_ValidTransition_UpdatesStatus(ApplicationStatus newStatus)
    {
        var app = MakeApplication();
        app.UpdateStatus(newStatus, "Good notes");
        app.Status.Should().Be(newStatus);
        app.StatusChangedAt.Should().NotBeNull();
    }

    [Fact]
    public void UpdateStatus_WithRejectionReason_SetsReason()
    {
        var app = MakeApplication();
        app.UpdateStatus(ApplicationStatus.Rejected, rejectionReason: "Not enough experience.");
        app.RejectionReason.Should().Be("Not enough experience.");
    }

    [Fact]
    public void UpdateStatus_AfterHired_ThrowsDomainException()
    {
        var app = MakeApplication();
        app.UpdateStatus(ApplicationStatus.Hired);
        var act = () => app.UpdateStatus(ApplicationStatus.Rejected);
        act.Should().Throw<DomainException>().WithMessage("*hired*");
    }

    [Fact]
    public void UpdateStatus_AfterWithdrawn_ThrowsDomainException()
    {
        var app = MakeApplication();
        app.Withdraw();
        var act = () => app.UpdateStatus(ApplicationStatus.Screening);
        act.Should().Throw<DomainException>().WithMessage("*withdrawn*");
    }

    [Fact]
    public void Withdraw_ActiveApplication_SetsWithdrawnStatus()
    {
        var app = MakeApplication();
        app.Withdraw();
        app.Status.Should().Be(ApplicationStatus.Withdrawn);
        app.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Withdraw_AlreadyWithdrawn_ThrowsDomainException()
    {
        var app = MakeApplication();
        app.Withdraw();
        var act = () => app.Withdraw();
        act.Should().Throw<DomainException>().WithMessage("*already closed*");
    }

    [Fact]
    public void Withdraw_RejectedApplication_ThrowsDomainException()
    {
        var app = MakeApplication();
        app.UpdateStatus(ApplicationStatus.Rejected);
        var act = () => app.Withdraw();
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void IsActive_WithdrawnApplication_ReturnsFalse()
    {
        var app = MakeApplication();
        app.Withdraw();
        app.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_RejectedApplication_ReturnsFalse()
    {
        var app = MakeApplication();
        app.UpdateStatus(ApplicationStatus.Rejected);
        app.IsActive.Should().BeFalse();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// CandidatePlan — Subscription limits
// ════════════════════════════════════════════════════════════════════════════════

public sealed class CandidatePlanTests
{
    [Theory]
    [InlineData(CandidatePlan.Free,       5)]
    [InlineData(CandidatePlan.Lite,       20)]
    [InlineData(CandidatePlan.Pro,        int.MaxValue)]
    [InlineData(CandidatePlan.Enterprise, int.MaxValue)]
    public void MonthlyApplicationLimit_ReturnsCorrectLimit(CandidatePlan plan, int expected)
        => plan.MonthlyApplicationLimit().Should().Be(expected);

    [Theory]
    [InlineData(CandidatePlan.Free,  false)]
    [InlineData(CandidatePlan.Lite,  false)]
    [InlineData(CandidatePlan.Pro,   true)]
    [InlineData(CandidatePlan.Enterprise, true)]
    public void IsUnlimited_ReturnsCorrectValue(CandidatePlan plan, bool expected)
        => plan.IsUnlimited().Should().Be(expected);
}

// ════════════════════════════════════════════════════════════════════════════════
// ApplicationMapper
// ════════════════════════════════════════════════════════════════════════════════

public sealed class ApplicationMapperTests
{
    [Fact]
    public void ToDto_MapsAllFields()
    {
        var app = JobApplication.Create(Guid.NewGuid(), Guid.NewGuid(), "Cover letter", "https://cv.io/cv.pdf");
        var dto = ApplicationMapper.ToDto(app);

        dto.Id.Should().Be(app.Id);
        dto.JobId.Should().Be(app.JobId);
        dto.CandidateId.Should().Be(app.CandidateId);
        dto.Status.Should().Be("Applied");
        dto.CoverLetter.Should().Be("Cover letter");
        dto.CvUrl.Should().Be("https://cv.io/cv.pdf");
        dto.EmployerNotes.Should().BeNull();
        dto.RejectionReason.Should().BeNull();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// ApplyToJob — Validator
// ════════════════════════════════════════════════════════════════════════════════

public sealed class ApplyToJobCommandValidatorTests
{
    private readonly ApplyToJobCommandValidator _sut = new();

    private static ApplyToJobCommand Valid() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Great role, excited to apply!", "https://cv.example.com/cv.pdf");

    [Fact]
    public void Validate_ValidCommand_Passes()
        => _sut.Validate(Valid()).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_EmptyJobId_Fails()
        => _sut.Validate(Valid() with { JobId = Guid.Empty }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_EmptyCandidateId_Fails()
        => _sut.Validate(Valid() with { CandidateId = Guid.Empty }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_CoverLetterTooLong_Fails()
        => _sut.Validate(Valid() with { CoverLetter = new string('x', 5_001) }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_NullCoverLetter_Passes()
        => _sut.Validate(Valid() with { CoverLetter = null }).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_InvalidCvUrl_Fails()
        => _sut.Validate(Valid() with { CvUrl = "not-a-url" }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_NullCvUrl_Passes()
        => _sut.Validate(Valid() with { CvUrl = null }).IsValid.Should().BeTrue();
}

// ════════════════════════════════════════════════════════════════════════════════
// ApplyToJob — Command Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class ApplyToJobCommandHandlerTests
{
    private readonly Mock<IApplicationRepository> _repo  = new();
    private readonly Mock<ISubscriptionService>   _subs  = new();
    private readonly Mock<IUnitOfWork>            _uow   = new();

    private ApplyToJobCommandHandler Handler() =>
        new(_repo.Object, _subs.Object, _uow.Object, NullLogger<ApplyToJobCommandHandler>.Instance);

    private void SetupFreePlan(int countThisMonth = 0)
    {
        _subs.Setup(s => s.GetCandidatePlanAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(CandidatePlan.Free);
        _repo.Setup(r => r.GetCountThisMonthAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(countThisMonth);
    }

    private void SetupProPlan()
    {
        _subs.Setup(s => s.GetCandidatePlanAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(CandidatePlan.Pro);
    }

    [Fact]
    public async Task Handle_ValidRequest_FreePlanUnderLimit_CreatesApplication()
    {
        SetupFreePlan(countThisMonth: 2);
        _repo.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);

        var dto = await Handler().Handle(
            new ApplyToJobCommand(Guid.NewGuid(), Guid.NewGuid(), "Great role!", null),
            CancellationToken.None);

        dto.Status.Should().Be("Applied");
        _repo.Verify(r => r.AddAsync(It.IsAny<JobApplication>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_FreePlanAtLimit_ThrowsSubscriptionLimitExceededException()
    {
        SetupFreePlan(countThisMonth: 5); // Free limit = 5
        _repo.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);

        var act = async () => await Handler().Handle(
            new ApplyToJobCommand(Guid.NewGuid(), Guid.NewGuid(), null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<SubscriptionLimitExceededException>()
            .WithMessage("*Free*5*");
    }

    [Fact]
    public async Task Handle_ProPlan_NoLimitCheck_CreatesApplication()
    {
        SetupProPlan();
        _repo.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);

        var dto = await Handler().Handle(
            new ApplyToJobCommand(Guid.NewGuid(), Guid.NewGuid(), null, null),
            CancellationToken.None);

        dto.Status.Should().Be("Applied");
        // GetCountThisMonthAsync should NOT be called for unlimited plans
        _repo.Verify(r => r.GetCountThisMonthAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DuplicateApplication_ThrowsDuplicateApplicationException()
    {
        SetupFreePlan();
        _repo.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);

        var jobId = Guid.NewGuid();
        var act = async () => await Handler().Handle(
            new ApplyToJobCommand(jobId, Guid.NewGuid(), null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<DuplicateApplicationException>()
            .WithMessage($"*{jobId}*");
    }

    [Fact]
    public async Task Handle_DuplicateCheck_RunsBeforeSubscriptionCheck()
    {
        // Even at limit, duplicate check should come first
        SetupFreePlan(countThisMonth: 5);
        _repo.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);

        var act = async () => await Handler().Handle(
            new ApplyToJobCommand(Guid.NewGuid(), Guid.NewGuid(), null, null),
            CancellationToken.None);

        // Should get DuplicateApplication, not SubscriptionLimitExceeded
        await act.Should().ThrowAsync<DuplicateApplicationException>();
    }

    [Fact]
    public async Task Handle_LitePlanAtLimit_ThrowsSubscriptionLimitExceededException()
    {
        _subs.Setup(s => s.GetCandidatePlanAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(CandidatePlan.Lite);
        _repo.Setup(r => r.GetCountThisMonthAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(20); // Lite limit = 20
        _repo.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);

        var act = async () => await Handler().Handle(
            new ApplyToJobCommand(Guid.NewGuid(), Guid.NewGuid(), null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<SubscriptionLimitExceededException>()
            .WithMessage("*Lite*20*");
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// UpdateApplicationStatus — Validator
// ════════════════════════════════════════════════════════════════════════════════

public sealed class UpdateApplicationStatusCommandValidatorTests
{
    private readonly UpdateApplicationStatusCommandValidator _sut = new();

    private static UpdateApplicationStatusCommand Valid() =>
        new(Guid.NewGuid(), Guid.NewGuid(), ApplicationStatus.Screening, null, null);

    [Fact]
    public void Validate_ValidCommand_Passes()
        => _sut.Validate(Valid()).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_EmptyApplicationId_Fails()
        => _sut.Validate(Valid() with { ApplicationId = Guid.Empty }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_Rejected_WithoutReason_Fails()
    {
        var result = _sut.Validate(Valid() with { NewStatus = ApplicationStatus.Rejected, RejectionReason = null });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateApplicationStatusCommand.RejectionReason));
    }

    [Fact]
    public void Validate_Rejected_WithReason_Passes()
        => _sut.Validate(Valid() with { NewStatus = ApplicationStatus.Rejected, RejectionReason = "Not enough experience." })
               .IsValid.Should().BeTrue();

    [Fact]
    public void Validate_Hired_WithoutReason_Passes()
        => _sut.Validate(Valid() with { NewStatus = ApplicationStatus.Hired }).IsValid.Should().BeTrue();
}

// ════════════════════════════════════════════════════════════════════════════════
// UpdateApplicationStatus — Command Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class UpdateApplicationStatusCommandHandlerTests
{
    private readonly Mock<IApplicationRepository> _repo = new();
    private readonly Mock<IUnitOfWork>            _uow  = new();

    private UpdateApplicationStatusCommandHandler Handler() =>
        new(_repo.Object, _uow.Object, NullLogger<UpdateApplicationStatusCommandHandler>.Instance);

    [Fact]
    public async Task Handle_ValidRequest_UpdatesStatus()
    {
        var app = JobApplication.Create(Guid.NewGuid(), Guid.NewGuid(), null, null);
        _repo.Setup(r => r.GetByIdAsync(app.Id, It.IsAny<CancellationToken>())).ReturnsAsync(app);

        var dto = await Handler().Handle(
            new UpdateApplicationStatusCommand(app.Id, Guid.NewGuid(), ApplicationStatus.Interview, "Strong candidate", null),
            CancellationToken.None);

        dto.Status.Should().Be("Interview");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotFound_ThrowsApplicationNotFoundException()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((JobApplication?)null);

        var act = async () => await Handler().Handle(
            new UpdateApplicationStatusCommand(Guid.NewGuid(), Guid.NewGuid(), ApplicationStatus.Screening, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ApplicationNotFoundException>();
    }

    [Fact]
    public async Task Handle_HiredApplication_ThrowsDomainException()
    {
        var app = JobApplication.Create(Guid.NewGuid(), Guid.NewGuid(), null, null);
        app.UpdateStatus(ApplicationStatus.Hired);
        _repo.Setup(r => r.GetByIdAsync(app.Id, It.IsAny<CancellationToken>())).ReturnsAsync(app);

        var act = async () => await Handler().Handle(
            new UpdateApplicationStatusCommand(app.Id, Guid.NewGuid(), ApplicationStatus.Rejected, null, "Late change"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*hired*");
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Withdraw — Validator + Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class WithdrawApplicationCommandValidatorTests
{
    private readonly WithdrawApplicationCommandValidator _sut = new();

    [Fact]
    public void Validate_ValidCommand_Passes()
        => _sut.Validate(new WithdrawApplicationCommand(Guid.NewGuid(), Guid.NewGuid())).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_EmptyApplicationId_Fails()
        => _sut.Validate(new WithdrawApplicationCommand(Guid.Empty, Guid.NewGuid())).IsValid.Should().BeFalse();
}

public sealed class WithdrawApplicationCommandHandlerTests
{
    private readonly Mock<IApplicationRepository> _repo = new();
    private readonly Mock<IUnitOfWork>            _uow  = new();

    private WithdrawApplicationCommandHandler Handler() =>
        new(_repo.Object, _uow.Object, NullLogger<WithdrawApplicationCommandHandler>.Instance);

    [Fact]
    public async Task Handle_OwnApplication_Withdraws()
    {
        var candidateId = Guid.NewGuid();
        var app = JobApplication.Create(Guid.NewGuid(), candidateId, null, null);
        _repo.Setup(r => r.GetByIdAsync(app.Id, It.IsAny<CancellationToken>())).ReturnsAsync(app);

        await Handler().Handle(new WithdrawApplicationCommand(app.Id, candidateId), CancellationToken.None);

        app.Status.Should().Be(ApplicationStatus.Withdrawn);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_OtherCandidateApplication_ThrowsAccessDeniedException()
    {
        var app = JobApplication.Create(Guid.NewGuid(), Guid.NewGuid(), null, null);
        _repo.Setup(r => r.GetByIdAsync(app.Id, It.IsAny<CancellationToken>())).ReturnsAsync(app);

        var act = async () => await Handler().Handle(
            new WithdrawApplicationCommand(app.Id, Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<ApplicationAccessDeniedException>();
    }

    [Fact]
    public async Task Handle_NotFound_ThrowsApplicationNotFoundException()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((JobApplication?)null);

        var act = async () => await Handler().Handle(
            new WithdrawApplicationCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<ApplicationNotFoundException>();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// GetMyApplications — Query Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class GetMyApplicationsQueryHandlerTests
{
    private readonly Mock<IApplicationRepository> _repo = new();
    private GetMyApplicationsQueryHandler Handler() => new(_repo.Object);

    [Fact]
    public async Task Handle_ReturnsPagedResult()
    {
        var candidateId = Guid.NewGuid();
        var apps = new List<JobApplication>
        {
            JobApplication.Create(Guid.NewGuid(), candidateId, null, null),
            JobApplication.Create(Guid.NewGuid(), candidateId, null, null),
        };
        _repo.Setup(r => r.GetByCandidateAsync(candidateId, 1, 20, default)).ReturnsAsync((apps, 2));

        var result = await Handler().Handle(new GetMyApplicationsQuery(candidateId), CancellationToken.None);

        result.Total.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.TotalPages.Should().Be(1);
        result.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NoApplications_ReturnEmptyPagedResult()
    {
        _repo.Setup(r => r.GetByCandidateAsync(It.IsAny<Guid>(), 1, 20, default))
             .ReturnsAsync((new List<JobApplication>(), 0));

        var result = await Handler().Handle(
            new GetMyApplicationsQuery(Guid.NewGuid()), CancellationToken.None);

        result.Total.Should().Be(0);
        result.TotalPages.Should().Be(0);
        result.Items.Should().BeEmpty();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// GetApplicationById — Query Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class GetApplicationByIdQueryHandlerTests
{
    private readonly Mock<IApplicationRepository> _repo = new();
    private GetApplicationByIdQueryHandler Handler() => new(_repo.Object);

    [Fact]
    public async Task Handle_CandidateViewsOwnApplication_ReturnsDto()
    {
        var candidateId = Guid.NewGuid();
        var app = JobApplication.Create(Guid.NewGuid(), candidateId, null, null);
        _repo.Setup(r => r.GetByIdAsync(app.Id, It.IsAny<CancellationToken>())).ReturnsAsync(app);

        var dto = await Handler().Handle(
            new GetApplicationByIdQuery(app.Id, candidateId, IsEmployer: false),
            CancellationToken.None);

        dto.Id.Should().Be(app.Id);
        dto.CandidateId.Should().Be(candidateId);
    }

    [Fact]
    public async Task Handle_CandidateViewsOtherApplication_ThrowsAccessDeniedException()
    {
        var app = JobApplication.Create(Guid.NewGuid(), Guid.NewGuid(), null, null);
        _repo.Setup(r => r.GetByIdAsync(app.Id, It.IsAny<CancellationToken>())).ReturnsAsync(app);

        var act = async () => await Handler().Handle(
            new GetApplicationByIdQuery(app.Id, Guid.NewGuid(), IsEmployer: false),
            CancellationToken.None);

        await act.Should().ThrowAsync<ApplicationAccessDeniedException>();
    }

    [Fact]
    public async Task Handle_EmployerViewsAnyApplication_ReturnsDto()
    {
        var app = JobApplication.Create(Guid.NewGuid(), Guid.NewGuid(), null, null);
        _repo.Setup(r => r.GetByIdAsync(app.Id, It.IsAny<CancellationToken>())).ReturnsAsync(app);

        // Employer with different ID can still view
        var dto = await Handler().Handle(
            new GetApplicationByIdQuery(app.Id, Guid.NewGuid(), IsEmployer: true),
            CancellationToken.None);

        dto.Id.Should().Be(app.Id);
    }

    [Fact]
    public async Task Handle_NotFound_ThrowsApplicationNotFoundException()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((JobApplication?)null);

        var act = async () => await Handler().Handle(
            new GetApplicationByIdQuery(Guid.NewGuid(), Guid.NewGuid(), false),
            CancellationToken.None);

        await act.Should().ThrowAsync<ApplicationNotFoundException>();
    }
}
