using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SNHub.Auth.Application.Commands.RegisterUser;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Domain.Entities;
using SNHub.Auth.Domain.Enums;
using SNHub.Auth.Domain.Exceptions;
using Xunit;

namespace SNHub.Auth.UnitTests.Commands;

public sealed class RegisterUserCommandHandlerTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<ITokenService> _tokens = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<ILogger<RegisterUserCommandHandler>> _logger = new();

    private RegisterUserCommandHandler Handler() => new(
        _users.Object, _uow.Object, _hasher.Object,
        _tokens.Object, _email.Object,
        _currentUser.Object, _logger.Object);

    private static RegisterUserCommand ValidCommand() => new(
        Email: "john@example.com",
        Password: "SecureP@ss1",
        ConfirmPassword: "SecureP@ss1",
        FirstName: "John",
        LastName: "Doe",
        Role: UserRole.Candidate);

    private void SetupHappyPath()
    {
        _users.Setup(r => r.ExistsByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _hasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns("hashed");
        _tokens.Setup(t => t.GenerateAccessToken(It.IsAny<User>())).Returns("access_token");
        _tokens.Setup(t => t.GenerateRefreshToken()).Returns("refresh_token");
        _currentUser.Setup(c => c.IpAddress).Returns("127.0.0.1");
        _currentUser.Setup(c => c.UserAgent).Returns("TestAgent");
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _email.Setup(e => e.SendEmailVerificationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Handle_ValidCommand_ReturnsAuthResponse()
    {
        SetupHappyPath();

        var result = await Handler().Handle(ValidCommand(), CancellationToken.None);

        result.Should().NotBeNull();
        result.AccessToken.Should().Be("access_token");
        result.RefreshToken.Should().Be("refresh_token");
        result.User.Email.Should().Be("john@example.com");
        result.User.Roles.Should().Contain("Candidate");
    }

    [Fact]
    public async Task Handle_DuplicateEmail_ThrowsUserAlreadyExistsException()
    {
        _users.Setup(r => r.ExistsByEmailAsync("john@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var act = () => Handler().Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<UserAlreadyExistsException>()
            .WithMessage("*john@example.com*");
    }

    [Fact]
    public async Task Handle_ValidCommand_AddsUserOnce()
    {
        SetupHappyPath();

        await Handler().Handle(ValidCommand(), CancellationToken.None);

        _users.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(UserRole.SuperAdmin)]
    [InlineData(UserRole.Moderator)]
    public async Task Validator_PrivilegedRole_Fails(UserRole role)
    {
        var validator = new RegisterUserCommandValidator();
        var cmd = ValidCommand() with { Role = role };

        var result = await validator.ValidateAsync(cmd);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Role");
    }

    [Theory]
    [InlineData("")]
    [InlineData("notanemail")]
    public async Task Validator_InvalidEmail_Fails(string email)
    {
        var validator = new RegisterUserCommandValidator();
        var cmd = ValidCommand() with { Email = email };

        var result = await validator.ValidateAsync(cmd);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validator_WeakPassword_Fails()
    {
        var validator = new RegisterUserCommandValidator();
        var cmd = ValidCommand() with { Password = "weak", ConfirmPassword = "weak" };

        var result = await validator.ValidateAsync(cmd);

        result.IsValid.Should().BeFalse();
    }
}
