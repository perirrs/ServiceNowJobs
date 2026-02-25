using SNHub.Matching.Application.DTOs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SNHub.Matching.Application.Commands.ProcessEmbedding;
using SNHub.Matching.Application.Commands.RequestEmbedding;
using SNHub.Matching.Application.Interfaces;
using SNHub.Matching.Application.Queries.GetCandidateMatches;
using SNHub.Matching.Application.Queries.GetJobMatches;
using SNHub.Matching.Domain.Entities;
using SNHub.Matching.Domain.Enums;
using SNHub.Matching.Domain.Exceptions;
using SNHub.Matching.Infrastructure.Services;
using Xunit;

namespace SNHub.Matching.UnitTests;

// ════════════════════════════════════════════════════════════════════════════
// Domain Entity — EmbeddingRecord
// ════════════════════════════════════════════════════════════════════════════

public sealed class EmbeddingRecordTests
{
    [Fact]
    public void Create_SetsPendingStatus()
    {
        var r = EmbeddingRecord.Create(Guid.NewGuid(), DocumentType.Job);
        r.Status.Should().Be(EmbeddingStatus.Pending);
        r.RetryCount.Should().Be(0);
        r.LastIndexedAt.Should().BeNull();
        r.CanRetry.Should().BeTrue();
    }

    [Fact]
    public void SetProcessing_ChangesStatus()
    {
        var r = EmbeddingRecord.Create(Guid.NewGuid(), DocumentType.Job);
        r.SetProcessing();
        r.Status.Should().Be(EmbeddingStatus.Processing);
    }

    [Fact]
    public void SetIndexed_SetsStatusAndTimestamp()
    {
        var r = EmbeddingRecord.Create(Guid.NewGuid(), DocumentType.CandidateProfile);
        r.SetProcessing();
        r.SetIndexed();
        r.Status.Should().Be(EmbeddingStatus.Indexed);
        r.LastIndexedAt.Should().NotBeNull();
        r.RetryCount.Should().Be(0);
        r.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void SetFailed_IncrementsRetryCount()
    {
        var r = EmbeddingRecord.Create(Guid.NewGuid(), DocumentType.Job);
        r.SetFailed("timeout");
        r.Status.Should().Be(EmbeddingStatus.Failed);
        r.RetryCount.Should().Be(1);
        r.ErrorMessage.Should().Be("timeout");
        r.CanRetry.Should().BeTrue();
    }

    [Fact]
    public void SetFailed_ThreeTimes_CannotRetry()
    {
        var r = EmbeddingRecord.Create(Guid.NewGuid(), DocumentType.Job);
        r.SetFailed("err1");
        r.SetFailed("err2");
        r.SetFailed("err3");
        r.CanRetry.Should().BeFalse();
    }

    [Fact]
    public void ResetToPending_RestoresStatus()
    {
        var r = EmbeddingRecord.Create(Guid.NewGuid(), DocumentType.Job);
        r.SetIndexed();
        r.ResetToPending();
        r.Status.Should().Be(EmbeddingStatus.Pending);
    }
}

// ════════════════════════════════════════════════════════════════════════════
// RequestEmbedding Command
// ════════════════════════════════════════════════════════════════════════════

public sealed class RequestEmbeddingCommandValidatorTests
{
    private readonly RequestEmbeddingCommandValidator _sut = new();

    [Fact]
    public void Validate_Valid_Passes()
        => _sut.Validate(new RequestEmbeddingCommand(Guid.NewGuid(), DocumentType.Job))
               .IsValid.Should().BeTrue();

    [Fact]
    public void Validate_EmptyGuid_Fails()
        => _sut.Validate(new RequestEmbeddingCommand(Guid.Empty, DocumentType.Job))
               .IsValid.Should().BeFalse();
}

public sealed class RequestEmbeddingCommandHandlerTests
{
    private readonly Mock<IEmbeddingRecordRepository> _repo = new();
    private readonly Mock<IUnitOfWork>                _uow  = new();

    private RequestEmbeddingCommandHandler Handler() => new(
        _repo.Object, _uow.Object,
        NullLogger<RequestEmbeddingCommandHandler>.Instance);

    [Fact]
    public async Task Handle_NewDocument_CreatesRecord()
    {
        _repo.Setup(r => r.GetByDocumentIdAsync(It.IsAny<Guid>(), It.IsAny<DocumentType>(), default))
             .ReturnsAsync((EmbeddingRecord?)null);

        var cmd = new RequestEmbeddingCommand(Guid.NewGuid(), DocumentType.Job);
        var dto = await Handler().Handle(cmd, CancellationToken.None);

        dto.Status.Should().Be("Pending");
        dto.DocumentType.Should().Be("Job");
        _repo.Verify(r => r.AddAsync(It.IsAny<EmbeddingRecord>(), default), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_ExistingDocument_ResetsToPending()
    {
        var existing = EmbeddingRecord.Create(Guid.NewGuid(), DocumentType.Job);
        existing.SetIndexed();
        _repo.Setup(r => r.GetByDocumentIdAsync(It.IsAny<Guid>(), It.IsAny<DocumentType>(), default))
             .ReturnsAsync(existing);

        await Handler().Handle(
            new RequestEmbeddingCommand(existing.DocumentId, DocumentType.Job),
            CancellationToken.None);

        existing.Status.Should().Be(EmbeddingStatus.Pending);
        _repo.Verify(r => r.AddAsync(It.IsAny<EmbeddingRecord>(), default), Times.Never);
    }
}

// ════════════════════════════════════════════════════════════════════════════
// ProcessEmbedding Command
// ════════════════════════════════════════════════════════════════════════════

public sealed class ProcessEmbeddingCommandHandlerTests
{
    private readonly Mock<IEmbeddingRecordRepository> _repo     = new();
    private readonly Mock<IUnitOfWork>                _uow      = new();
    private readonly Mock<IEmbeddingService>          _emb      = new();
    private readonly Mock<IVectorSearchService>       _search   = new();
    private readonly Mock<IJobsServiceClient>         _jobs     = new();
    private readonly Mock<IProfilesServiceClient>     _profiles = new();

    private ProcessEmbeddingCommandHandler Handler() => new(
        _repo.Object, _uow.Object, _emb.Object, _search.Object,
        _jobs.Object, _profiles.Object,
        NullLogger<ProcessEmbeddingCommandHandler>.Instance);

    private static float[] FakeVector() => new float[1536];

    [Fact]
    public async Task Handle_Job_FetchesAndIndexes()
    {
        var jobId  = Guid.NewGuid();
        var record = EmbeddingRecord.Create(jobId, DocumentType.Job);
        _repo.Setup(r => r.GetByDocumentIdAsync(jobId, DocumentType.Job, default))
             .ReturnsAsync(record);
        _jobs.Setup(j => j.GetJobAsync(jobId, default)).ReturnsAsync(new JobData
        {
            Id = jobId, Title = "SNow Developer", Description = "Build flows",
            IsActive = true, WorkMode = "Remote", ExperienceLevel = "Senior",
            JobType = "FullTime", Skills = ["ITSM", "HRSD"],
            CreatedAt = DateTimeOffset.UtcNow
        });
        _emb.Setup(e => e.GenerateAsync(It.IsAny<string>(), default))
            .ReturnsAsync(FakeVector());

        var result = await Handler().Handle(
            new ProcessEmbeddingCommand(jobId, DocumentType.Job), CancellationToken.None);

        result.Should().BeTrue();
        record.Status.Should().Be(EmbeddingStatus.Indexed);
        _search.Verify(s => s.UpsertJobAsync(It.IsAny<JobSearchDocument>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_InactiveJob_DeletesFromIndex()
    {
        var jobId  = Guid.NewGuid();
        var record = EmbeddingRecord.Create(jobId, DocumentType.Job);
        _repo.Setup(r => r.GetByDocumentIdAsync(jobId, DocumentType.Job, default))
             .ReturnsAsync(record);
        _jobs.Setup(j => j.GetJobAsync(jobId, default)).ReturnsAsync(new JobData
        {
            Id = jobId, Title = "Closed", Description = "x",
            IsActive = false, WorkMode = "Remote", ExperienceLevel = "Mid",
            JobType = "FullTime", CreatedAt = DateTimeOffset.UtcNow
        });

        await Handler().Handle(
            new ProcessEmbeddingCommand(jobId, DocumentType.Job), CancellationToken.None);

        _search.Verify(s => s.DeleteAsync(jobId.ToString(), DocumentType.Job, default), Times.Once);
        _emb.Verify(e => e.GenerateAsync(It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_Candidate_FetchesAndIndexes()
    {
        var userId = Guid.NewGuid();
        var record = EmbeddingRecord.Create(userId, DocumentType.CandidateProfile);
        _repo.Setup(r => r.GetByDocumentIdAsync(userId, DocumentType.CandidateProfile, default))
             .ReturnsAsync(record);
        _profiles.Setup(p => p.GetCandidateAsync(userId, default)).ReturnsAsync(new CandidateData
        {
            UserId = userId, FirstName = "Jane", Headline = "SNow Architect",
            Skills = ["ITSM", "HRSD"], ExperienceLevel = "Senior",
            Availability = "OpenToOpportunities", UpdatedAt = DateTimeOffset.UtcNow
        });
        _emb.Setup(e => e.GenerateAsync(It.IsAny<string>(), default))
            .ReturnsAsync(FakeVector());

        var result = await Handler().Handle(
            new ProcessEmbeddingCommand(userId, DocumentType.CandidateProfile),
            CancellationToken.None);

        result.Should().BeTrue();
        record.Status.Should().Be(EmbeddingStatus.Indexed);
        _search.Verify(s => s.UpsertCandidateAsync(It.IsAny<CandidateSearchDocument>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_EmbeddingServiceFails_SetsFailedStatus()
    {
        var jobId  = Guid.NewGuid();
        var record = EmbeddingRecord.Create(jobId, DocumentType.Job);
        _repo.Setup(r => r.GetByDocumentIdAsync(jobId, DocumentType.Job, default))
             .ReturnsAsync(record);
        _jobs.Setup(j => j.GetJobAsync(jobId, default)).ReturnsAsync(new JobData
        {
            Id = jobId, Title = "x", Description = "x", IsActive = true,
            WorkMode = "Remote", ExperienceLevel = "Mid", JobType = "FullTime",
            CreatedAt = DateTimeOffset.UtcNow
        });
        _emb.Setup(e => e.GenerateAsync(It.IsAny<string>(), default))
            .ThrowsAsync(new Exception("OpenAI rate limit"));

        var result = await Handler().Handle(
            new ProcessEmbeddingCommand(jobId, DocumentType.Job), CancellationToken.None);

        result.Should().BeFalse();
        record.Status.Should().Be(EmbeddingStatus.Failed);
        record.ErrorMessage.Should().Contain("OpenAI rate limit");
    }

    [Fact]
    public async Task Handle_RecordNotFound_ReturnsFalse()
    {
        _repo.Setup(r => r.GetByDocumentIdAsync(It.IsAny<Guid>(), It.IsAny<DocumentType>(), default))
             .ReturnsAsync((EmbeddingRecord?)null);

        var result = await Handler().Handle(
            new ProcessEmbeddingCommand(Guid.NewGuid(), DocumentType.Job),
            CancellationToken.None);

        result.Should().BeFalse();
    }
}

// ════════════════════════════════════════════════════════════════════════════
// GetJobMatches Query
// ════════════════════════════════════════════════════════════════════════════

public sealed class GetJobMatchesQueryValidatorTests
{
    private readonly GetJobMatchesQueryValidator _sut = new();

    [Fact]
    public void Validate_Valid_Passes()
        => _sut.Validate(new GetJobMatchesQuery(Guid.NewGuid(), Guid.NewGuid(), false))
               .IsValid.Should().BeTrue();

    [Fact]
    public void Validate_EmptyJobId_Fails()
        => _sut.Validate(new GetJobMatchesQuery(Guid.Empty, Guid.NewGuid(), false))
               .IsValid.Should().BeFalse();

    [Fact]
    public void Validate_PageSizeOver50_Fails()
        => _sut.Validate(new GetJobMatchesQuery(Guid.NewGuid(), Guid.NewGuid(), false, 1, 51))
               .IsValid.Should().BeFalse();
}

public sealed class GetJobMatchesQueryHandlerTests
{
    private readonly Mock<IEmbeddingRecordRepository> _repo     = new();
    private readonly Mock<IVectorSearchService>       _search   = new();
    private readonly Mock<IJobsServiceClient>         _jobs     = new();
    private readonly Mock<IProfilesServiceClient>     _profiles = new();

    private GetJobMatchesQueryHandler Handler() => new(
        _repo.Object, _search.Object, _jobs.Object, _profiles.Object,
        NullLogger<GetJobMatchesQueryHandler>.Instance);

    [Fact]
    public async Task Handle_EmbeddingNotIndexed_ReturnsNotReady()
    {
        var jobId    = Guid.NewGuid();
        var employer = Guid.NewGuid();
        _jobs.Setup(j => j.GetJobAsync(jobId, default)).ReturnsAsync(
            new JobData { Id = jobId, EmployerId = employer, Title = "x",
                Description = "x", IsActive = true, WorkMode = "Remote",
                ExperienceLevel = "Mid", JobType = "FullTime", CreatedAt = DateTimeOffset.UtcNow });
        _repo.Setup(r => r.GetByDocumentIdAsync(jobId, DocumentType.Job, default))
             .ReturnsAsync((EmbeddingRecord?)null);

        var result = await Handler().Handle(
            new GetJobMatchesQuery(jobId, employer, false), CancellationToken.None);

        result.EmbeddingReady.Should().BeFalse();
        result.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_DifferentEmployer_ThrowsAccessDenied()
    {
        var jobId    = Guid.NewGuid();
        var employer = Guid.NewGuid();
        _jobs.Setup(j => j.GetJobAsync(jobId, default)).ReturnsAsync(
            new JobData { Id = jobId, EmployerId = employer, Title = "x",
                Description = "x", IsActive = true, WorkMode = "Remote",
                ExperienceLevel = "Mid", JobType = "FullTime", CreatedAt = DateTimeOffset.UtcNow });

        var act = async () => await Handler().Handle(
            new GetJobMatchesQuery(jobId, Guid.NewGuid(), false), // different user
            CancellationToken.None);

        await act.Should().ThrowAsync<AccessDeniedException>();
    }

    [Fact]
    public async Task Handle_AdminBypassesOwnershipCheck()
    {
        var jobId = Guid.NewGuid();
        _jobs.Setup(j => j.GetJobAsync(jobId, default)).ReturnsAsync(
            new JobData { Id = jobId, EmployerId = Guid.NewGuid(), Title = "x",
                Description = "x", IsActive = true, WorkMode = "Remote",
                ExperienceLevel = "Mid", JobType = "FullTime", CreatedAt = DateTimeOffset.UtcNow });
        _repo.Setup(r => r.GetByDocumentIdAsync(jobId, DocumentType.Job, default))
             .ReturnsAsync((EmbeddingRecord?)null);

        // Admin with different userId — should NOT throw
        var result = await Handler().Handle(
            new GetJobMatchesQuery(jobId, Guid.NewGuid(), RequesterIsAdmin: true),
            CancellationToken.None);

        result.EmbeddingReady.Should().BeFalse(); // not indexed but no exception
    }
}

// ════════════════════════════════════════════════════════════════════════════
// GetCandidateMatches Query
// ════════════════════════════════════════════════════════════════════════════

public sealed class GetCandidateMatchesQueryValidatorTests
{
    private readonly GetCandidateMatchesQueryValidator _sut = new();

    [Fact]
    public void Validate_Valid_Passes()
        => _sut.Validate(new GetCandidateMatchesQuery(Guid.NewGuid()))
               .IsValid.Should().BeTrue();

    [Fact]
    public void Validate_EmptyUserId_Fails()
        => _sut.Validate(new GetCandidateMatchesQuery(Guid.Empty))
               .IsValid.Should().BeFalse();
}

public sealed class GetCandidateMatchesQueryHandlerTests
{
    private readonly Mock<IEmbeddingRecordRepository> _repo     = new();
    private readonly Mock<IVectorSearchService>       _search   = new();
    private readonly Mock<IProfilesServiceClient>     _profiles = new();

    private GetCandidateMatchesQueryHandler Handler() => new(
        _repo.Object, _search.Object, _profiles.Object,
        NullLogger<GetCandidateMatchesQueryHandler>.Instance);

    [Fact]
    public async Task Handle_EmbeddingNotIndexed_ReturnsNotReady()
    {
        _repo.Setup(r => r.GetByDocumentIdAsync(
                It.IsAny<Guid>(), DocumentType.CandidateProfile, default))
             .ReturnsAsync((EmbeddingRecord?)null);

        var result = await Handler().Handle(
            new GetCandidateMatchesQuery(Guid.NewGuid()), CancellationToken.None);

        result.EmbeddingReady.Should().BeFalse();
        result.Results.Should().BeEmpty();
    }
}

// ════════════════════════════════════════════════════════════════════════════
// StubEmbeddingService
// ════════════════════════════════════════════════════════════════════════════

public sealed class StubEmbeddingServiceTests
{
    private readonly StubEmbeddingService _sut = new();

    [Fact]
    public async Task GenerateAsync_Returns1536DimVector()
    {
        var vector = await _sut.GenerateAsync("ServiceNow ITSM developer");
        vector.Should().HaveCount(1536);
    }

    [Fact]
    public async Task GenerateAsync_VectorIsNormalised()
    {
        var vector = await _sut.GenerateAsync("test text");
        var magnitude = Math.Sqrt(vector.Sum(v => (double)v * v));
        magnitude.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public async Task GenerateAsync_SameInputSameOutput()
    {
        var v1 = await _sut.GenerateAsync("ServiceNow ITSM");
        var v2 = await _sut.GenerateAsync("ServiceNow ITSM");
        v1.Should().BeEquivalentTo(v2);
    }

    [Fact]
    public async Task GenerateAsync_DifferentInputDifferentOutput()
    {
        var v1 = await _sut.GenerateAsync("ServiceNow ITSM");
        var v2 = await _sut.GenerateAsync("Java microservices");
        v1.Should().NotBeEquivalentTo(v2);
    }
}

// ════════════════════════════════════════════════════════════════════════════
// InMemoryVectorSearchService
// ════════════════════════════════════════════════════════════════════════════

public sealed class InMemoryVectorSearchServiceTests
{
    private readonly InMemoryVectorSearchService _sut = new();

    private static JobSearchDocument MakeJob(string id) => new()
    {
        Id = id, Title = "Dev", Description = "x",
        WorkMode = "Remote", ExperienceLevel = "Mid", JobType = "FullTime",
        Skills = ["ITSM"], ServiceNowVersions = [],
        Embedding = new float[1536], CreatedAt = DateTimeOffset.UtcNow
    };

    private static CandidateSearchDocument MakeCandidate(string id) => new()
    {
        Id = id, ExperienceLevel = "Senior", Availability = "OpenToOpportunities",
        Skills = ["ITSM"], Certifications = [], ServiceNowVersions = [],
        Embedding = new float[1536], UpdatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task UpsertAndSearch_Jobs_ReturnsResults()
    {
        await _sut.UpsertJobAsync(MakeJob("job-1"));
        await _sut.UpsertJobAsync(MakeJob("job-2"));

        var results = (await _sut.SearchJobsForCandidateAsync([], 10)).ToList();
        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task Delete_RemovesFromIndex()
    {
        await _sut.UpsertJobAsync(MakeJob("job-del"));
        await _sut.DeleteAsync("job-del", DocumentType.Job);

        var results = await _sut.SearchJobsForCandidateAsync([], 10);
        results.Should().NotContain(r => r.JobId == "job-del");
    }

    [Fact]
    public async Task UpsertAndSearch_Candidates_ReturnsResults()
    {
        await _sut.UpsertCandidateAsync(MakeCandidate("cand-1"));

        var results = (await _sut.SearchCandidatesForJobAsync([], 10)).ToList();
        results.Should().HaveCount(1);
        results[0].CandidateId.Should().Be("cand-1");
    }
}
