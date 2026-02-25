namespace SNHub.JobEnhancer.Domain.Exceptions;

public class DomainException(string msg) : Exception(msg);
public sealed class EnhancementNotFoundException(Guid id)
    : Exception($"Enhancement {id} not found.");
public sealed class EnhancementAccessDeniedException()
    : Exception("You do not have access to this enhancement.");
public sealed class EnhancementNotCompletedException()
    : Exception("Enhancement has not completed yet.");
public sealed class EnhancementAlreadyAcceptedException()
    : Exception("This enhancement has already been accepted.");
