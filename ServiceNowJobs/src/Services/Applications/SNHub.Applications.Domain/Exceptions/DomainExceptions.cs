namespace SNHub.Applications.Domain.Exceptions;

public sealed class DomainException(string message) : Exception(message);
public sealed class ApplicationNotFoundException(Guid id) : Exception($"Application {id} not found.");
public sealed class DuplicateApplicationException(Guid jobId) : Exception($"You have already applied to job {jobId}.");
public sealed class InvalidStatusTransitionException(string from, string to) : Exception($"Cannot move from {from} to {to}.");
public sealed class SubscriptionLimitExceededException(string plan, int limit)
    : Exception($"Your {plan} plan allows {limit} application(s) per month. Upgrade to apply to more jobs.");
public sealed class ApplicationAccessDeniedException()
    : Exception("You do not have permission to access this application.");
