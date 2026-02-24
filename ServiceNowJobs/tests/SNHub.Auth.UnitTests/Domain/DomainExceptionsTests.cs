using FluentAssertions;
using SNHub.Auth.Domain.Exceptions;
using Xunit;

namespace SNHub.Auth.UnitTests.Domain;

public sealed class DomainExceptionsTests
{
    // ── DomainException ───────────────────────────────────────────────────────

    [Fact]
    public void DomainException_StoresMessage()
    {
        var ex = new DomainException("Something went wrong.");
        ex.Message.Should().Be("Something went wrong.");
    }

    [Fact]
    public void DomainException_IsException()
    {
        var ex = new DomainException("test");
        ex.Should().BeAssignableTo<Exception>();
    }

    // ── UserAlreadyExistsException ────────────────────────────────────────────

    [Fact]
    public void UserAlreadyExistsException_ContainsEmail()
    {
        var ex = new UserAlreadyExistsException("john@example.com");
        ex.Email.Should().Be("john@example.com");
        ex.Message.Should().Contain("john@example.com");
    }

    [Fact]
    public void UserAlreadyExistsException_IsDomainException()
    {
        var ex = new UserAlreadyExistsException("u@x.com");
        ex.Should().BeAssignableTo<DomainException>();
    }

    // ── UserNotFoundException ─────────────────────────────────────────────────

    [Fact]
    public void UserNotFoundException_ByGuid_ContainsId()
    {
        var id = Guid.NewGuid();
        var ex = new UserNotFoundException(id);
        ex.Message.Should().Contain(id.ToString());
    }

    [Fact]
    public void UserNotFoundException_ByEmail_ContainsEmail()
    {
        var ex = new UserNotFoundException("ghost@x.com");
        ex.Message.Should().Contain("ghost@x.com");
    }

    // ── InvalidCredentialsException ───────────────────────────────────────────

    [Fact]
    public void InvalidCredentialsException_HasMessage()
    {
        var ex = new InvalidCredentialsException();
        ex.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void InvalidCredentialsException_IsDomainException()
    {
        var ex = new InvalidCredentialsException();
        ex.Should().BeAssignableTo<DomainException>();
    }

    // ── AccountLockedException ────────────────────────────────────────────────

    [Fact]
    public void AccountLockedException_StoresLockedUntil()
    {
        var until = DateTimeOffset.UtcNow.AddMinutes(30);
        var ex    = new AccountLockedException(until);
        ex.LockedUntil.Should().Be(until);
    }

    [Fact]
    public void AccountLockedException_NullLockedUntil_IsAccepted()
    {
        var ex = new AccountLockedException(null);
        ex.LockedUntil.Should().BeNull();
    }

    // ── AccountSuspendedException ─────────────────────────────────────────────

    [Fact]
    public void AccountSuspendedException_ContainsReason()
    {
        var ex = new AccountSuspendedException("Violation of ToS.");
        ex.Message.Should().Contain("Violation of ToS.");
    }

    [Fact]
    public void AccountSuspendedException_NullReason_FallsBackToDefault()
    {
        var ex = new AccountSuspendedException(null);
        ex.Message.Should().Contain("Contact support");
    }

    // ── EmailNotVerifiedException ─────────────────────────────────────────────

    [Fact]
    public void EmailNotVerifiedException_HasMessage()
    {
        var ex = new EmailNotVerifiedException();
        ex.Message.Should().NotBeNullOrWhiteSpace();
    }

    // ── InvalidTokenException ─────────────────────────────────────────────────

    [Fact]
    public void InvalidTokenException_StoresMessage()
    {
        var ex = new InvalidTokenException("Token expired.");
        ex.Message.Should().Be("Token expired.");
    }

    [Fact]
    public void InvalidTokenException_IsDomainException()
    {
        var ex = new InvalidTokenException("bad");
        ex.Should().BeAssignableTo<DomainException>();
    }
}
