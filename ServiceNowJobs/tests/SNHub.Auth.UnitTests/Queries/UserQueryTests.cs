using FluentAssertions;
using Moq;
using SNHub.Auth.Application.DTOs;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Application.Queries.GetCurrentUser;
using SNHub.Auth.Application.Queries.GetUserById;
using SNHub.Auth.Application.Queries.GetUsers;
using SNHub.Auth.Domain.Entities;
using SNHub.Auth.Domain.Enums;
using SNHub.Auth.Domain.Exceptions;
using Xunit;

namespace SNHub.Auth.UnitTests.Queries;

// ── GetCurrentUser ──────────────────────────────────────────────────────────

public sealed class GetCurrentUserQueryHandlerTests
{
    private readonly Mock<IUserRepository>     _users       = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();

    private GetCurrentUserQueryHandler Handler() =>
        new(_users.Object, _currentUser.Object);

    private static User MakeUser() => User.Create(
        "jane@example.com", "hash", "Jane", "Doe", UserRole.Candidate);

    [Fact]
    public async Task Handle_AuthenticatedUser_ReturnsProfile()
    {
        var user   = MakeUser();
        _currentUser.Setup(x => x.UserId).Returns(user.Id);
        _currentUser.Setup(x => x.Email).Returns(user.Email);
        _users.Setup(x => x.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
              .ReturnsAsync(user);

        var result = await Handler().Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.Email.Should().Be(user.Email);
        result.FirstName.Should().Be("Jane");
        result.LastName.Should().Be("Doe");
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ThrowsUnauthorized()
    {
        _currentUser.Setup(x => x.UserId).Returns((Guid?)null);

        var act = async () => await Handler().Handle(
            new GetCurrentUserQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Handle_UserNotFound_ThrowsUserNotFoundException()
    {
        var id = Guid.NewGuid();
        _currentUser.Setup(x => x.UserId).Returns(id);
        _users.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
              .ReturnsAsync((User?)null);

        var act = async () => await Handler().Handle(
            new GetCurrentUserQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<UserNotFoundException>();
    }

    [Fact]
    public async Task Handle_MapsAllProfileFields()
    {
        var user = MakeUser();
        _currentUser.Setup(x => x.UserId).Returns(user.Id);
        _users.Setup(x => x.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
              .ReturnsAsync(user);

        var result = await Handler().Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.Id.Should().Be(user.Id);
        result.FullName.Should().Be("Jane Doe");
        result.IsEmailVerified.Should().BeFalse();
        result.IsActive.Should().BeTrue();
        result.Roles.Should().Contain("Candidate");
    }
}

// ── GetUserById ─────────────────────────────────────────────────────────────

public sealed class GetUserByIdQueryHandlerTests
{
    private readonly Mock<IUserRepository> _users = new();

    private GetUserByIdQueryHandler Handler() => new(_users.Object);

    private static User MakeUser() => User.Create(
        "admin@example.com", "hash", "Admin", "User", UserRole.Employer);

    [Fact]
    public async Task Handle_ExistingUser_ReturnsAdminDto()
    {
        var user = MakeUser();
        _users.Setup(x => x.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
              .ReturnsAsync(user);

        var result = await Handler().Handle(
            new GetUserByIdQuery(user.Id), CancellationToken.None);

        result.Id.Should().Be(user.Id);
        result.Email.Should().Be(user.Email);
        result.IsSuspended.Should().BeFalse();
        result.FailedLoginAttempts.Should().Be(0);
    }

    [Fact]
    public async Task Handle_UserNotFound_ThrowsUserNotFoundException()
    {
        var id = Guid.NewGuid();
        _users.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
              .ReturnsAsync((User?)null);

        var act = async () => await Handler().Handle(
            new GetUserByIdQuery(id), CancellationToken.None);

        await act.Should().ThrowAsync<UserNotFoundException>();
    }

    [Fact]
    public async Task Handle_SuspendedUser_IncludesSuspensionDetails()
    {
        var user = MakeUser();
        user.Suspend("Test reason", "admin");
        _users.Setup(x => x.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
              .ReturnsAsync(user);

        var result = await Handler().Handle(
            new GetUserByIdQuery(user.Id), CancellationToken.None);

        result.IsSuspended.Should().BeTrue();
        result.SuspensionReason.Should().Be("Test reason");
    }
}

// ── GetUsers ─────────────────────────────────────────────────────────────────

public sealed class GetUsersQueryHandlerTests
{
    private readonly Mock<IUserRepository> _users = new();

    private GetUsersQueryHandler Handler() => new(_users.Object);

    [Fact]
    public async Task Handle_ReturnsPagedResult()
    {
        var items = new List<UserSummaryDto>
        {
            new(Guid.NewGuid(), "a@b.com", "A", "B", "A B",
                true, false, false, new[] { "Candidate" }, DateTimeOffset.UtcNow),
        };

        _users.Setup(x => x.GetPagedAsync(
                1, 20, null, null, null, It.IsAny<CancellationToken>()))
              .ReturnsAsync((items, 1));

        var result = await Handler().Handle(
            new GetUsersQuery(1, 20), CancellationToken.None);

        result.TotalCount.Should().Be(1);
        result.Items.Should().HaveCount(1);
        result.TotalPages.Should().Be(1);
    }

    [Fact]
    public async Task Handle_EmptyResult_ReturnsEmptyPage()
    {
        _users.Setup(x => x.GetPagedAsync(
                1, 20, null, null, null, It.IsAny<CancellationToken>()))
              .ReturnsAsync((new List<UserSummaryDto>(), 0));

        var result = await Handler().Handle(
            new GetUsersQuery(1, 20), CancellationToken.None);

        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
        result.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_PassesFiltersToRepository()
    {
        _users.Setup(x => x.GetPagedAsync(
                2, 10, UserRole.Employer, true, "john", It.IsAny<CancellationToken>()))
              .ReturnsAsync((new List<UserSummaryDto>(), 0));

        await Handler().Handle(
            new GetUsersQuery(2, 10, "john", true, UserRole.Employer),
            CancellationToken.None);

        _users.Verify(x => x.GetPagedAsync(
            2, 10, UserRole.Employer, true, "john", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Validator ──

    [Fact]
    public void Validator_PageZero_Fails()
    {
        var v = new GetUsersQueryValidator();
        var result = v.Validate(new GetUsersQuery(Page: 0));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Page");
    }

    [Fact]
    public void Validator_PageSizeTooLarge_Fails()
    {
        var v = new GetUsersQueryValidator();
        var result = v.Validate(new GetUsersQuery(PageSize: 101));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_ValidQuery_Passes()
    {
        var v = new GetUsersQueryValidator();
        var result = v.Validate(new GetUsersQuery(1, 50));
        result.IsValid.Should().BeTrue();
    }
}
