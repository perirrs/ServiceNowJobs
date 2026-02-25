namespace SNHub.Matching.Domain.Exceptions;

public sealed class DomainException(string msg) : Exception(msg);
public sealed class DocumentNotFoundException(Guid id, string type)
    : Exception($"{type} document {id} not found.");
public sealed class AccessDeniedException()
    : Exception("You do not have access to this resource.");
public sealed class EmbeddingNotReadyException(Guid documentId)
    : Exception($"Embedding for document {documentId} is not yet indexed. Try again shortly.");
