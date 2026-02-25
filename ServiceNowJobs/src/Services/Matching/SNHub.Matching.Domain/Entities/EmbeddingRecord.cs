using SNHub.Matching.Domain.Enums;

namespace SNHub.Matching.Domain.Entities;

/// <summary>
/// Tracks the embedding state for a single document (Job or CandidateProfile).
/// When a document changes, its status is set to Pending and the background
/// worker picks it up, generates an embedding, and stores it in Azure AI Search.
/// </summary>
public sealed class EmbeddingRecord
{
    private EmbeddingRecord() { }

    public Guid           Id           { get; private set; }
    public Guid           DocumentId   { get; private set; }   // JobId or UserId
    public DocumentType   DocumentType { get; private set; }
    public EmbeddingStatus Status      { get; private set; }
    public string?        ErrorMessage { get; private set; }
    public int            RetryCount   { get; private set; }
    public DateTimeOffset? LastIndexedAt { get; private set; }
    public DateTimeOffset CreatedAt    { get; private set; }
    public DateTimeOffset UpdatedAt    { get; private set; }

    public static EmbeddingRecord Create(Guid documentId, DocumentType type) => new()
    {
        Id           = Guid.NewGuid(),
        DocumentId   = documentId,
        DocumentType = type,
        Status       = EmbeddingStatus.Pending,
        RetryCount   = 0,
        CreatedAt    = DateTimeOffset.UtcNow,
        UpdatedAt    = DateTimeOffset.UtcNow
    };

    public void SetProcessing()
    {
        Status    = EmbeddingStatus.Processing;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetIndexed()
    {
        Status        = EmbeddingStatus.Indexed;
        LastIndexedAt = DateTimeOffset.UtcNow;
        RetryCount    = 0;
        ErrorMessage  = null;
        UpdatedAt     = DateTimeOffset.UtcNow;
    }

    public void SetFailed(string error)
    {
        Status       = EmbeddingStatus.Failed;
        ErrorMessage = error;
        RetryCount++;
        UpdatedAt    = DateTimeOffset.UtcNow;
    }

    public void ResetToPending()
    {
        Status    = EmbeddingStatus.Pending;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public bool CanRetry => RetryCount < 3;
}
