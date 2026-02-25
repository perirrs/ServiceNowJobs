using SNHub.Matching.Application.DTOs;
using SNHub.Matching.Domain.Entities;
using SNHub.Matching.Domain.Enums;

namespace SNHub.Matching.Application.Interfaces;

// ── Repository ────────────────────────────────────────────────────────────────

public interface IEmbeddingRecordRepository
{
    Task<EmbeddingRecord?> GetByDocumentIdAsync(Guid documentId, DocumentType type, CancellationToken ct = default);
    Task<IEnumerable<EmbeddingRecord>> GetPendingAsync(int batchSize, CancellationToken ct = default);
    Task AddAsync(EmbeddingRecord record, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid documentId, DocumentType type, CancellationToken ct = default);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

// ── Vector search ─────────────────────────────────────────────────────────────

public interface IVectorSearchService
{
    Task UpsertJobAsync(JobSearchDocument doc, CancellationToken ct = default);
    Task UpsertCandidateAsync(CandidateSearchDocument doc, CancellationToken ct = default);
    Task DeleteAsync(string id, DocumentType type, CancellationToken ct = default);

    /// <summary>Find top-N jobs semantically similar to a candidate embedding.</summary>
    Task<IEnumerable<(string JobId, double Score)>> SearchJobsForCandidateAsync(
        float[] candidateEmbedding, int topK = 20, CancellationToken ct = default);

    /// <summary>Find top-N candidates semantically similar to a job embedding.</summary>
    Task<IEnumerable<(string CandidateId, double Score)>> SearchCandidatesForJobAsync(
        float[] jobEmbedding, int topK = 20, CancellationToken ct = default);
}

// ── Embedding generation ──────────────────────────────────────────────────────

public interface IEmbeddingService
{
    /// <summary>Generate a 1536-dim embedding vector for the given text.</summary>
    Task<float[]> GenerateAsync(string text, CancellationToken ct = default);
}

// ── Cross-service data clients ────────────────────────────────────────────────

public interface IJobsServiceClient
{
    Task<JobData?> GetJobAsync(Guid jobId, CancellationToken ct = default);
}

public interface IProfilesServiceClient
{
    Task<CandidateData?> GetCandidateAsync(Guid userId, CancellationToken ct = default);
    Task<IEnumerable<CandidateData>> GetPublicCandidatesAsync(CancellationToken ct = default);
}

public interface ICurrentUserService
{
    Guid? UserId { get; }
    bool  IsAuthenticated { get; }
    bool  IsInRole(string role);
}

// ── Data transfer objects from external services ──────────────────────────────

public sealed class JobData
{
    public Guid    Id              { get; set; }
    public Guid    EmployerId      { get; set; }
    public string  Title           { get; set; } = string.Empty;
    public string  Description     { get; set; } = string.Empty;
    public string? Requirements    { get; set; }
    public string? CompanyName     { get; set; }
    public string? Location        { get; set; }
    public string? Country         { get; set; }
    public string  WorkMode        { get; set; } = string.Empty;
    public string  ExperienceLevel { get; set; } = string.Empty;
    public string  JobType         { get; set; } = string.Empty;
    public decimal? SalaryMin      { get; set; }
    public decimal? SalaryMax      { get; set; }
    public string?  SalaryCurrency { get; set; }
    public bool    IsSalaryVisible { get; set; }
    public string[] Skills         { get; set; } = [];
    public string[] ServiceNowVersions { get; set; } = [];
    public bool    IsActive        { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CandidateData
{
    public Guid    UserId           { get; set; }
    public string? FirstName        { get; set; }
    public string? LastName         { get; set; }
    public string? Headline         { get; set; }
    public string? Bio              { get; set; }
    public string? CurrentRole      { get; set; }
    public string? Location         { get; set; }
    public string? Country          { get; set; }
    public int     YearsOfExperience { get; set; }
    public string  ExperienceLevel  { get; set; } = string.Empty;
    public string  Availability     { get; set; } = string.Empty;
    public bool    OpenToRemote     { get; set; }
    public string[] Skills          { get; set; } = [];
    public string[] Certifications  { get; set; } = [];
    public string[] ServiceNowVersions { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; }
}
