using SNHub.Auth.Domain.Enums;

namespace SNHub.Auth.Domain.Events;

public abstract record DomainEvent(DateTimeOffset OccurredAt)
{
    protected DomainEvent() : this(DateTimeOffset.UtcNow) { }
}

public sealed record UserRegisteredEvent(
    Guid UserId,
    string Email,
    UserRole Role) : DomainEvent;

public sealed record UserLoggedInEvent(
    Guid UserId,
    string Email,
    string IpAddress) : DomainEvent;

public sealed record UserEmailVerifiedEvent(
    Guid UserId,
    string Email) : DomainEvent;

public sealed record UserPasswordResetEvent(
    Guid UserId,
    string Email) : DomainEvent;

public sealed record UserSuspendedEvent(
    Guid UserId,
    string Email,
    string Reason) : DomainEvent;
