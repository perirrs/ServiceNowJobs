namespace SNHub.Auth.Domain.Exceptions;

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}

public sealed class UserAlreadyExistsException : DomainException
{
    public string Email { get; }
    public UserAlreadyExistsException(string email)
        : base($"A user with email '{email}' already exists.")
    {
        Email = email;
    }
}

public sealed class UserNotFoundException : DomainException
{
    public UserNotFoundException(Guid userId)
        : base($"User '{userId}' was not found.") { }

    public UserNotFoundException(string email)
        : base($"User '{email}' was not found.") { }
}

public sealed class InvalidCredentialsException : DomainException
{
    public InvalidCredentialsException()
        : base("The provided credentials are invalid.") { }
}

public sealed class AccountLockedException : DomainException
{
    public DateTimeOffset? LockedUntil { get; }
    public AccountLockedException(DateTimeOffset? lockedUntil)
        : base($"Account is locked until {lockedUntil:u}.")
    {
        LockedUntil = lockedUntil;
    }
}

public sealed class AccountSuspendedException : DomainException
{
    public AccountSuspendedException(string? reason)
        : base($"Account suspended. Reason: {reason ?? "Contact support."}") { }
}

public sealed class EmailNotVerifiedException : DomainException
{
    public EmailNotVerifiedException()
        : base("Email address has not been verified. Please check your inbox.") { }
}

public sealed class InvalidTokenException : DomainException
{
    public InvalidTokenException(string message) : base(message) { }
}
