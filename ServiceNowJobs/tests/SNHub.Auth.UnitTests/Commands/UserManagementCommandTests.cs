using FluentAssertions;
using Moq;
using SNHub.Auth.Application.Commands.ReinstateUser;
using SNHub.Auth.Application.Commands.SuspendUser;
using SNHub.Auth.Application.Commands.UpdateProfile;
using SNHub.Auth.Application.Commands.UpdateUserRoles;
using SNHub.Auth.Application.Commands.UploadProfilePicture;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Domain.Entities;
using SNHub.Auth.Domain.Enums;
using SNHub.Auth.Domain.Exceptions;
using Xunit;

namespace SNHub.Auth.UnitTests.Commands;

// ── UpdateProfile ─────────────────────────────────────────────────────────────

public sealed class UpdateProfileCommandHandlerTests
{
    private readonly Mock<IUserRepository>     _users       = new();
    private readonly Mock<IUnitOfWork>         _uow         = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();

    private UpdateProfileCommandHandler Handler() =>
        new(_users.Object, _uow.Object, _currentUser.Object);

    private static User MakeUser() => User.Create(
        "jane@example.com", "hash", "Jane", "Doe", UserRole.Candidate);

    [Fact]
    public async Task Handle_ValidUpdate_ReturnsMutatedProfile()
    {
        var user = MakeUser();
        _currentUser.Setup(x => x.Email).Returns(user.Email);
        _users.Setup(x => x.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
              .ReturnsAsync(user);

        var cmd = new UpdateProfileCommand("NewFirst", "NewLast", "+441234567890", "GB", "Europe/London");
        var result = await Handler().Handle(cmd, CancellationToken.None);

        result.FirstName.Should().Be("NewFirst");
        result.LastName.Should().Be("NewLast");
        result.PhoneNumber.Should().Be("+441234567890");
        result.Country.Should().Be("GB");
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NoEmail_ThrowsUnauthorized()
    {
        _currentUser.Setup(x => x.Email).Returns((string?)null);

        var act = async () => await Handler().Handle(
            new UpdateProfileCommand("F", "L"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Handle_UserNotFound_ThrowsUserNotFoundException()
    {
        _currentUser.Setup(x => x.Email).Returns("ghost@example.com");
        _users.Setup(x => x.GetByEmailAsync("ghost@example.com", It.IsAny<CancellationToken>()))
              .ReturnsAsync((User?)null);

        var act = async () => await Handler().Handle(
            new UpdateProfileCommand("F", "L"), CancellationToken.None);

        await act.Should().ThrowAsync<UserNotFoundException>();
    }

    // ── Validator ──

    [Theory]
    [InlineData("", "Last", null, null, null)]       // empty first name
    [InlineData("First", "", null, null, null)]       // empty last name
    [InlineData("F1rst", "Last", null, null, null)]  // number in name
    public void Validator_InvalidInputs_Fail(
        string first, string last, string? phone, string? country, string? tz)
    {
        var v = new UpdateProfileCommandValidator();
        var result = v.Validate(new UpdateProfileCommand(first, last, phone, country, tz));
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("Jane", "Doe", null, null, null)]
    [InlineData("Mary-Jane", "O'Brien", "+447911123456", "GB", "Europe/London")]
    [InlineData("José", "García", null, "ES", null)]
    public void Validator_ValidInputs_Pass(
        string first, string last, string? phone, string? country, string? tz)
    {
        var v = new UpdateProfileCommandValidator();
        var result = v.Validate(new UpdateProfileCommand(first, last, phone, country, tz));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_InvalidPhone_Fails()
    {
        var v = new UpdateProfileCommandValidator();
        var result = v.Validate(new UpdateProfileCommand("F", "L", "not-a-phone"));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PhoneNumber");
    }
}

// ── UploadProfilePicture ──────────────────────────────────────────────────────

public sealed class UploadProfilePictureCommandValidatorTests
{
    private readonly UploadProfilePictureCommandValidator _validator = new();

    [Fact]
    public void Validator_ValidJpeg_Passes()
    {
        var result = _validator.Validate(
            new UploadProfilePictureCommand(new byte[1024], "photo.jpg", "image/jpeg"));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_EmptyFile_Fails()
    {
        var result = _validator.Validate(
            new UploadProfilePictureCommand(Array.Empty<byte>(), "photo.jpg", "image/jpeg"));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FileBytes");
    }

    [Fact]
    public void Validator_FileTooBig_Fails()
    {
        var result = _validator.Validate(
            new UploadProfilePictureCommand(new byte[6 * 1024 * 1024], "photo.jpg", "image/jpeg"));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_UnsupportedType_Fails()
    {
        var result = _validator.Validate(
            new UploadProfilePictureCommand(new byte[1024], "file.gif", "image/gif"));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ContentType");
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/jpg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public void Validator_AllowedContentTypes_Pass(string contentType)
    {
        var result = _validator.Validate(
            new UploadProfilePictureCommand(new byte[1024], "photo", contentType));
        result.IsValid.Should().BeTrue();
    }
}

// ── SuspendUser ───────────────────────────────────────────────────────────────

public sealed class SuspendUserCommandHandlerTests
{
    private readonly Mock<IUserRepository>     _users       = new();
    private readonly Mock<IUnitOfWork>         _uow         = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();

    private SuspendUserCommandHandler Handler() =>
        new(_users.Object, _uow.Object, _currentUser.Object);

    private static User MakeUser() =>
        User.Create("target@example.com", "hash", "Target", "User", UserRole.Candidate);

    [Fact]
    public async Task Handle_ValidTarget_SuspendsAndSaves()
    {
        var adminId = Guid.NewGuid();
        var user    = MakeUser();

        _currentUser.Setup(x => x.UserId).Returns(adminId);
        _currentUser.Setup(x => x.Email).Returns("admin@snhub.io");
        _users.Setup(x => x.GetByIdWithTokensAsync(user.Id, It.IsAny<CancellationToken>()))
              .ReturnsAsync(user);

        await Handler().Handle(
            new SuspendUserCommand(user.Id, "Spamming."), CancellationToken.None);

        user.IsSuspended.Should().BeTrue();
        user.SuspensionReason.Should().Be("Spamming.");
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SelfSuspend_ThrowsDomainException()
    {
        var adminId = Guid.NewGuid();
        _currentUser.Setup(x => x.UserId).Returns(adminId);

        var act = async () => await Handler().Handle(
            new SuspendUserCommand(adminId, "test"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*cannot suspend your own*");
    }

    [Fact]
    public async Task Handle_UserNotFound_ThrowsUserNotFoundException()
    {
        var adminId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        _currentUser.Setup(x => x.UserId).Returns(adminId);
        _users.Setup(x => x.GetByIdWithTokensAsync(targetId, It.IsAny<CancellationToken>()))
              .ReturnsAsync((User?)null);

        var act = async () => await Handler().Handle(
            new SuspendUserCommand(targetId, "reason"), CancellationToken.None);

        await act.Should().ThrowAsync<UserNotFoundException>();
    }

    // ── Validator ──

    [Fact]
    public void Validator_EmptyReason_Fails()
    {
        var v = new SuspendUserCommandValidator();
        var r = v.Validate(new SuspendUserCommand(Guid.NewGuid(), ""));
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_ValidCommand_Passes()
    {
        var v = new SuspendUserCommandValidator();
        var r = v.Validate(new SuspendUserCommand(Guid.NewGuid(), "Terms violation."));
        r.IsValid.Should().BeTrue();
    }
}

// ── ReinstateUser ─────────────────────────────────────────────────────────────

public sealed class ReinstateUserCommandHandlerTests
{
    private readonly Mock<IUserRepository>     _users       = new();
    private readonly Mock<IUnitOfWork>         _uow         = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();

    private ReinstateUserCommandHandler Handler() =>
        new(_users.Object, _uow.Object, _currentUser.Object);

    [Fact]
    public async Task Handle_SuspendedUser_ReinstatesAndSaves()
    {
        var user = User.Create("u@example.com", "h", "U", "U", UserRole.Candidate);
        user.Suspend("test", "admin");
        _users.Setup(x => x.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
              .ReturnsAsync(user);

        await Handler().Handle(new ReinstateUserCommand(user.Id), CancellationToken.None);

        user.IsSuspended.Should().BeFalse();
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotSuspended_ThrowsDomainException()
    {
        var user = User.Create("u@example.com", "h", "U", "U", UserRole.Candidate);
        _users.Setup(x => x.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
              .ReturnsAsync(user);

        var act = async () => await Handler().Handle(
            new ReinstateUserCommand(user.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*not suspended*");
    }

    [Fact]
    public async Task Handle_UserNotFound_ThrowsUserNotFoundException()
    {
        var id = Guid.NewGuid();
        _users.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
              .ReturnsAsync((User?)null);

        var act = async () => await Handler().Handle(
            new ReinstateUserCommand(id), CancellationToken.None);

        await act.Should().ThrowAsync<UserNotFoundException>();
    }
}

// ── UpdateUserRoles ───────────────────────────────────────────────────────────

public sealed class UpdateUserRolesCommandHandlerTests
{
    private readonly Mock<IUserRepository>     _users       = new();
    private readonly Mock<IUnitOfWork>         _uow         = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();

    private UpdateUserRolesCommandHandler Handler() =>
        new(_users.Object, _uow.Object, _currentUser.Object);

    [Fact]
    public async Task Handle_ValidRoles_UpdatesAndSaves()
    {
        var adminId = Guid.NewGuid();
        var user    = User.Create("u@example.com", "h", "U", "U", UserRole.Candidate);

        _currentUser.Setup(x => x.UserId).Returns(adminId);
        _users.Setup(x => x.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
              .ReturnsAsync(user);

        await Handler().Handle(
            new UpdateUserRolesCommand(user.Id, new[] { UserRole.Employer }),
            CancellationToken.None);

        user.Roles.Should().Contain(UserRole.Employer);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SelfUpdate_ThrowsDomainException()
    {
        var id = Guid.NewGuid();
        _currentUser.Setup(x => x.UserId).Returns(id);

        var act = async () => await Handler().Handle(
            new UpdateUserRolesCommand(id, new[] { UserRole.Candidate }),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*own roles*");
    }

    [Fact]
    public async Task Handle_TargetIsSuperAdmin_ThrowsDomainException()
    {
        var adminId = Guid.NewGuid();
        var user    = User.Create("s@example.com", "h", "S", "A", UserRole.Candidate);
        user.AddRole(UserRole.SuperAdmin);

        _currentUser.Setup(x => x.UserId).Returns(adminId);
        _users.Setup(x => x.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
              .ReturnsAsync(user);

        var act = async () => await Handler().Handle(
            new UpdateUserRolesCommand(user.Id, new[] { UserRole.Candidate }),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*SuperAdmin*");
    }

    [Fact]
    public async Task Handle_UserNotFound_ThrowsUserNotFoundException()
    {
        var adminId  = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        _currentUser.Setup(x => x.UserId).Returns(adminId);
        _users.Setup(x => x.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
              .ReturnsAsync((User?)null);

        var act = async () => await Handler().Handle(
            new UpdateUserRolesCommand(targetId, new[] { UserRole.Candidate }),
            CancellationToken.None);

        await act.Should().ThrowAsync<UserNotFoundException>();
    }

    // ── Validator ──

    [Fact]
    public void Validator_SuperAdminRole_Fails()
    {
        var v = new UpdateUserRolesCommandValidator();
        var r = v.Validate(new UpdateUserRolesCommand(Guid.NewGuid(), new[] { UserRole.SuperAdmin }));
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == "Roles");
    }

    [Fact]
    public void Validator_EmptyRoles_Fails()
    {
        var v = new UpdateUserRolesCommandValidator();
        var r = v.Validate(new UpdateUserRolesCommand(Guid.NewGuid(), Array.Empty<UserRole>()));
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_ValidRoles_Passes()
    {
        var v = new UpdateUserRolesCommandValidator();
        var r = v.Validate(new UpdateUserRolesCommand(
            Guid.NewGuid(), new[] { UserRole.Employer, UserRole.HiringManager }));
        r.IsValid.Should().BeTrue();
    }
}
