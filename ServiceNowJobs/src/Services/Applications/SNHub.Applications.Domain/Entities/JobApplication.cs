using SNHub.Applications.Domain.Enums;
using SNHub.Applications.Domain.Exceptions;

namespace SNHub.Applications.Domain.Entities;

public sealed class JobApplication
{
    private JobApplication() { }

    public Guid Id { get; private set; }
    public Guid JobId { get; private set; }
    public Guid CandidateId { get; private set; }
    public ApplicationStatus Status { get; private set; }
    public string? CoverLetter { get; private set; }
    public string? CvUrl { get; private set; }
    public string? EmployerNotes { get; private set; }
    public DateTimeOffset AppliedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? StatusChangedAt { get; private set; }
    public string? RejectionReason { get; private set; }

    public bool IsActive => Status != ApplicationStatus.Withdrawn && Status != ApplicationStatus.Rejected;

    public static JobApplication Create(Guid jobId, Guid candidateId, string? coverLetter, string? cvUrl) =>
        new()
        {
            Id          = Guid.NewGuid(),
            JobId       = jobId,
            CandidateId = candidateId,
            Status      = ApplicationStatus.Applied,
            CoverLetter = coverLetter?.Trim(),
            CvUrl       = cvUrl,
            AppliedAt   = DateTimeOffset.UtcNow,
            UpdatedAt   = DateTimeOffset.UtcNow
        };

    public void UpdateStatus(ApplicationStatus newStatus, string? notes = null, string? rejectionReason = null)
    {
        // Validate transitions
        if (Status == ApplicationStatus.Hired)    throw new DomainException("Cannot change status of a hired application.");
        if (Status == ApplicationStatus.Withdrawn) throw new DomainException("Cannot update a withdrawn application.");

        Status = newStatus;
        EmployerNotes = notes?.Trim() ?? EmployerNotes;
        RejectionReason = rejectionReason?.Trim();
        StatusChangedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Withdraw()
    {
        if (!IsActive) throw new DomainException("Application is already closed.");
        Status = ApplicationStatus.Withdrawn;
        StatusChangedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
