using SNHub.JobEnhancer.Application.DTOs;
using SNHub.JobEnhancer.Domain.Entities;

namespace SNHub.JobEnhancer.Application.Interfaces;

public interface IEnhancementResultRepository
{
    Task<EnhancementResult?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<EnhancementResult>> GetByJobIdAsync(Guid jobId, CancellationToken ct = default);
    Task<IEnumerable<EnhancementResult>> GetByRequesterAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(EnhancementResult result, CancellationToken ct = default);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>
/// Calls Azure OpenAI GPT-4o to enhance a job description.
/// Returns structured analysis including bias detection, quality scoring,
/// and rewritten content.
/// </summary>
public interface IJobDescriptionEnhancer
{
    Task<GptEnhancementResult> EnhanceAsync(
        string title,
        string description,
        string? requirements,
        CancellationToken ct = default);
}

/// <summary>
/// Notifies the Jobs service that an enhancement was accepted
/// so it can update the job posting.
/// </summary>
public interface IJobsServiceClient
{
    Task ApplyEnhancementAsync(
        Guid jobId,
        string? enhancedTitle,
        string? enhancedDescription,
        string? enhancedRequirements,
        string[] suggestedSkills,
        CancellationToken ct = default);
}

public interface ICurrentUserService
{
    Guid? UserId        { get; }
    bool  IsAuthenticated { get; }
    bool  IsInRole(string role);
}
