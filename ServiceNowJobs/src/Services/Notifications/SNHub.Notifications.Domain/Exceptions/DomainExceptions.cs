namespace SNHub.Notifications.Domain.Exceptions;

public sealed class DomainException(string msg) : Exception(msg);

public sealed class NotificationNotFoundException(Guid id)
    : Exception($"Notification {id} not found.");

public sealed class NotificationAccessDeniedException()
    : Exception("You do not have permission to access this notification.");
