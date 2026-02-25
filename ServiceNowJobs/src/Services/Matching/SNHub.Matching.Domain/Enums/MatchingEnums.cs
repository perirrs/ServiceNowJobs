namespace SNHub.Matching.Domain.Enums;

public enum DocumentType
{
    Job              = 1,
    CandidateProfile = 2
}

public enum EmbeddingStatus
{
    Pending    = 1,
    Processing = 2,
    Indexed    = 3,
    Failed     = 4
}
