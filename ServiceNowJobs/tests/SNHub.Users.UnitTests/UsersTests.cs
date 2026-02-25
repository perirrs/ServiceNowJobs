using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SNHub.Users.Application.Commands.ReinstateUser;
using SNHub.Users.Application.Commands.SoftDeleteUser;
using SNHub.Users.Application.Commands.UpdateProfile;
using SNHub.Users.Application.Commands.UploadProfilePicture;
using SNHub.Users.Application.DTOs;
using SNHub.Users.Application.Interfaces;
using SNHub.Users.Application.Queries.GetAdminUsers;
using SNHub.Users.Application.Queries.GetProfile;
using SNHub.Users.Domain.Entities;
using SNHub.Users.Domain.Exceptions;
using Xunit;

namespace SNHub.Users.UnitTests;

// ════════════════════════════════════════════════════════════════════════════════
// Domain Entity — UserProfile
// ════════════════════════════════════════════════════════════════════════════════

public sealed class UserProfileEntityTests
{
    [Fact]
    public void Create_SetsDefaults()
    {
        var userId = Guid.NewGuid();
        var p = UserProfile.Create(userId);

        p.Id.Should().NotBeEmpty();
        p.UserId.Should().Be(userId);
        p.IsPublic.Should().BeTrue();
        p.IsDeleted.Should().BeFalse();
        p.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Update_TrimsAndSetsAllFields()
    {
        var p = UserProfile.Create(Guid.NewGuid());
        p.Update("  John  ", "  Doe  ", "john@example.com", "07700000000",
            "  SNow Architect  ", "  Bio text  ", "  London  ",
            "https://linkedin.com/in/john", "https://github.com/john",
            "https://john.dev", 8, true, "GBR", "Europe/London");

        p.FirstName.Should().Be("John");
        p.LastName.Should().Be("Doe");
        p.Email.Should().Be("john@example.com");
        p.Headline.Should().Be("SNow Architect");
        p.Bio.Should().Be("Bio text");
        p.YearsOfExperience.Should().Be(8);
        p.Country.Should().Be("GBR");
    }

    [Fact]
    public void Update_EmailNormalisedToLowercase()
    {
        var p = UserProfile.Create(Guid.NewGuid());
        p.Update(null, null, "JOHN@EXAMPLE.COM", null, null, null,
            null, null, null, null, 0, true, null, null);

        p.Email.Should().Be("john@example.com");
    }

    [Fact]
    public void SetProfilePicture_UpdatesUrlAndUpdatedAt()
    {
        var p = UserProfile.Create(Guid.NewGuid());
        var before = p.UpdatedAt;

        p.SetProfilePicture("https://cdn.snhub.io/pic.jpg");

        p.ProfilePictureUrl.Should().Be("https://cdn.snhub.io/pic.jpg");
        p.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void SoftDelete_SetsDeletedFields()
    {
        var p = UserProfile.Create(Guid.NewGuid());
        var adminId = Guid.NewGuid();

        p.SoftDelete(adminId);

        p.IsDeleted.Should().BeTrue();
        p.DeletedAt.Should().NotBeNull();
        p.DeletedAt!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void SoftDelete_CalledTwice_IsIdempotent()
    {
        var p = UserProfile.Create(Guid.NewGuid());
        var adminId = Guid.NewGuid();
        p.SoftDelete(adminId);
        var firstDeletedAt = p.DeletedAt;

        p.SoftDelete(adminId); // second call — no-op

        p.DeletedAt.Should().Be(firstDeletedAt);
    }

    [Fact]
    public void Reinstate_ClearsDeletedFields()
    {
        var p = UserProfile.Create(Guid.NewGuid());
        p.SoftDelete(Guid.NewGuid());

        p.Reinstate();

        p.IsDeleted.Should().BeFalse();
        p.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void FullName_ConcatenatesFirstAndLastName()
    {
        var p = UserProfile.Create(Guid.NewGuid());
        p.Update("Jane", "Smith", null, null, null, null,
            null, null, null, null, 0, true, null, null);

        p.FullName.Should().Be("Jane Smith");
    }

    [Fact]
    public void FullName_OnlyFirstName_NoTrailingSpace()
    {
        var p = UserProfile.Create(Guid.NewGuid());
        p.Update("Jane", null, null, null, null, null,
            null, null, null, null, 0, true, null, null);

        p.FullName.Should().Be("Jane");
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// UserProfileMapper
// ════════════════════════════════════════════════════════════════════════════════

public sealed class UserProfileMapperTests
{
    [Fact]
    public void ToDto_MapsAllFields()
    {
        var p = UserProfile.Create(Guid.NewGuid());
        p.Update("Alice", "Walker", "alice@test.com", null, "ITSM Lead", "Bio",
            "London", "https://linkedin.com/in/alice", null, null, 5, true, "GBR", "UTC");

        var dto = UserProfileMapper.ToDto(p);

        dto.FirstName.Should().Be("Alice");
        dto.LastName.Should().Be("Walker");
        dto.Email.Should().Be("alice@test.com");
        dto.Headline.Should().Be("ITSM Lead");
        dto.Country.Should().Be("GBR");
        dto.IsPublic.Should().BeTrue();
        dto.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void ToAdminDto_MapsDeletedFields()
    {
        var p = UserProfile.Create(Guid.NewGuid());
        p.SoftDelete(Guid.NewGuid());

        var dto = UserProfileMapper.ToAdminDto(p);

        dto.IsDeleted.Should().BeTrue();
        dto.DeletedAt.Should().NotBeNull();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// UpdateProfile — Validator
// ════════════════════════════════════════════════════════════════════════════════

public sealed class UpdateProfileCommandValidatorTests
{
    private readonly UpdateProfileCommandValidator _sut = new();

    private static UpdateProfileCommand Valid() => new(
        Guid.NewGuid(), "John", "Doe", "07700000000",
        "SNow Architect", "My bio", "London",
        "https://linkedin.com/in/john", "https://github.com/john",
        "https://john.dev", 5, true, "GBR", "Europe/London");

    [Fact]
    public void Validate_ValidCommand_Passes()
        => _sut.Validate(Valid()).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_EmptyUserId_Fails()
        => _sut.Validate(Valid() with { UserId = Guid.Empty }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_FirstNameTooLong_Fails()
        => _sut.Validate(Valid() with { FirstName = new string('x', 101) }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_HeadlineTooLong_Fails()
        => _sut.Validate(Valid() with { Headline = new string('x', 201) }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_BioTooLong_Fails()
        => _sut.Validate(Valid() with { Bio = new string('x', 2_001) }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_NegativeYears_Fails()
        => _sut.Validate(Valid() with { YearsOfExperience = -1 }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_YearsOver50_Fails()
        => _sut.Validate(Valid() with { YearsOfExperience = 51 }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_InvalidLinkedIn_Fails()
        => _sut.Validate(Valid() with { LinkedInUrl = "not-a-url" }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_NullLinkedIn_Passes()
        => _sut.Validate(Valid() with { LinkedInUrl = null }).IsValid.Should().BeTrue();
}

// ════════════════════════════════════════════════════════════════════════════════
// UploadProfilePicture — Validator
// ════════════════════════════════════════════════════════════════════════════════

public sealed class UploadProfilePictureCommandValidatorTests
{
    private readonly UploadProfilePictureCommandValidator _sut = new();

    private static UploadProfilePictureCommand Valid() =>
        new(Guid.NewGuid(), Stream.Null, "photo.jpg", "image/jpeg", 1024);

    [Fact]
    public void Validate_ValidJpeg_Passes()
        => _sut.Validate(Valid()).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_ValidPng_Passes()
        => _sut.Validate(Valid() with { ContentType = "image/png" }).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_ValidWebp_Passes()
        => _sut.Validate(Valid() with { ContentType = "image/webp" }).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_PdfContentType_Fails()
        => _sut.Validate(Valid() with { ContentType = "application/pdf" }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_FileTooLarge_Fails()
        => _sut.Validate(Valid() with { FileSize = 6 * 1024 * 1024 }).IsValid.Should().BeFalse();
}

// ════════════════════════════════════════════════════════════════════════════════
// UpdateProfile — Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class UpdateProfileCommandHandlerTests
{
    private readonly Mock<IUserProfileRepository> _repo = new();
    private readonly Mock<IUnitOfWork>            _uow  = new();

    private UpdateProfileCommandHandler Handler() =>
        new(_repo.Object, _uow.Object, NullLogger<UpdateProfileCommandHandler>.Instance);

    private static UpdateProfileCommand Cmd(Guid userId) =>
        new(userId, "Alice", "Smith", null, "ITSM Lead", null, "London",
            null, null, null, 3, true, "GBR", null);

    [Fact]
    public async Task Handle_NewProfile_CreatesAndSaves()
    {
        var userId = Guid.NewGuid();
        _repo.Setup(r => r.GetByUserIdAsync(userId, default))
             .ReturnsAsync((UserProfile?)null);

        var dto = await Handler().Handle(Cmd(userId), CancellationToken.None);

        dto.FirstName.Should().Be("Alice");
        _repo.Verify(r => r.AddAsync(It.IsAny<UserProfile>(), default), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_ExistingProfile_UpdatesWithoutAdd()
    {
        var userId  = Guid.NewGuid();
        var profile = UserProfile.Create(userId);
        _repo.Setup(r => r.GetByUserIdAsync(userId, default)).ReturnsAsync(profile);

        var dto = await Handler().Handle(Cmd(userId), CancellationToken.None);

        dto.FirstName.Should().Be("Alice");
        _repo.Verify(r => r.AddAsync(It.IsAny<UserProfile>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_ExistingProfile_PreservesEmail()
    {
        var userId  = Guid.NewGuid();
        var profile = UserProfile.Create(userId);
        profile.Update(null, null, "existing@email.com", null, null, null,
            null, null, null, null, 0, true, null, null);
        _repo.Setup(r => r.GetByUserIdAsync(userId, default)).ReturnsAsync(profile);

        await Handler().Handle(Cmd(userId), CancellationToken.None);

        profile.Email.Should().Be("existing@email.com"); // email not overwritten
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// GetProfile — Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class GetProfileQueryHandlerTests
{
    private readonly Mock<IUserProfileRepository> _repo = new();
    private GetProfileQueryHandler Handler() => new(_repo.Object);

    [Fact]
    public async Task Handle_ExistingUser_ReturnsDto()
    {
        var userId  = Guid.NewGuid();
        var profile = UserProfile.Create(userId);
        profile.Update("Bob", null, null, null, null, null,
            null, null, null, null, 0, true, null, null);
        _repo.Setup(r => r.GetByUserIdAsync(userId, default)).ReturnsAsync(profile);

        var dto = await Handler().Handle(new GetProfileQuery(userId), CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.FirstName.Should().Be("Bob");
    }

    [Fact]
    public async Task Handle_NonExistentUser_ReturnsNull()
    {
        _repo.Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), default))
             .ReturnsAsync((UserProfile?)null);

        var dto = await Handler().Handle(
            new GetProfileQuery(Guid.NewGuid()), CancellationToken.None);

        dto.Should().BeNull();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// SoftDelete — Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class SoftDeleteUserCommandHandlerTests
{
    private readonly Mock<IUserProfileRepository> _repo = new();
    private readonly Mock<IUnitOfWork>            _uow  = new();

    private SoftDeleteUserCommandHandler Handler() =>
        new(_repo.Object, _uow.Object, NullLogger<SoftDeleteUserCommandHandler>.Instance);

    [Fact]
    public async Task Handle_ActiveUser_SoftDeletesAndSaves()
    {
        var userId  = Guid.NewGuid();
        var profile = UserProfile.Create(userId);
        _repo.Setup(r => r.GetByUserIdAsync(userId, default)).ReturnsAsync(profile);

        await Handler().Handle(new SoftDeleteUserCommand(userId, Guid.NewGuid()), CancellationToken.None);

        profile.IsDeleted.Should().BeTrue();
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyDeleted_ThrowsUserAlreadyDeletedException()
    {
        var userId  = Guid.NewGuid();
        var profile = UserProfile.Create(userId);
        profile.SoftDelete(Guid.NewGuid());
        _repo.Setup(r => r.GetByUserIdAsync(userId, default)).ReturnsAsync(profile);

        var act = async () => await Handler().Handle(
            new SoftDeleteUserCommand(userId, Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<UserAlreadyDeletedException>();
    }

    [Fact]
    public async Task Handle_NotFound_ThrowsUserProfileNotFoundException()
    {
        _repo.Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), default))
             .ReturnsAsync((UserProfile?)null);

        var act = async () => await Handler().Handle(
            new SoftDeleteUserCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<UserProfileNotFoundException>();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Reinstate — Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class ReinstateUserCommandHandlerTests
{
    private readonly Mock<IUserProfileRepository> _repo = new();
    private readonly Mock<IUnitOfWork>            _uow  = new();

    private ReinstateUserCommandHandler Handler() =>
        new(_repo.Object, _uow.Object, NullLogger<ReinstateUserCommandHandler>.Instance);

    [Fact]
    public async Task Handle_DeletedUser_Reinstates()
    {
        var userId  = Guid.NewGuid();
        var profile = UserProfile.Create(userId);
        profile.SoftDelete(Guid.NewGuid());
        _repo.Setup(r => r.GetByUserIdAsync(userId, default)).ReturnsAsync(profile);

        await Handler().Handle(
            new ReinstateUserCommand(userId, Guid.NewGuid()), CancellationToken.None);

        profile.IsDeleted.Should().BeFalse();
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_NotDeletedUser_Throws()
    {
        var userId  = Guid.NewGuid();
        var profile = UserProfile.Create(userId);  // not deleted
        _repo.Setup(r => r.GetByUserIdAsync(userId, default)).ReturnsAsync(profile);

        var act = async () => await Handler().Handle(
            new ReinstateUserCommand(userId, Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Handle_NotFound_Throws()
    {
        _repo.Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), default))
             .ReturnsAsync((UserProfile?)null);

        var act = async () => await Handler().Handle(
            new ReinstateUserCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<UserProfileNotFoundException>();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// GetAdminUsers — Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class GetAdminUsersQueryHandlerTests
{
    private readonly Mock<IUserProfileRepository> _repo = new();
    private GetAdminUsersQueryHandler Handler() => new(_repo.Object);

    [Fact]
    public async Task Handle_ReturnsPagedResult()
    {
        var profiles = new[] { UserProfile.Create(Guid.NewGuid()), UserProfile.Create(Guid.NewGuid()) };
        _repo.Setup(r => r.GetPagedAsync(null, null, 1, 20, default))
             .ReturnsAsync((profiles, 2));

        var result = await Handler().Handle(
            new GetAdminUsersQuery(null, null), CancellationToken.None);

        result.Total.Should().Be(2);
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_EmptyResults_ReturnsEmptyPaged()
    {
        _repo.Setup(r => r.GetPagedAsync(null, null, 1, 20, default))
             .ReturnsAsync((Enumerable.Empty<UserProfile>(), 0));

        var result = await Handler().Handle(
            new GetAdminUsersQuery(null, null), CancellationToken.None);

        result.Total.Should().Be(0);
        result.Items.Should().BeEmpty();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// PagedResult pagination math
// ════════════════════════════════════════════════════════════════════════════════

public sealed class PagedResultTests
{
    [Theory]
    [InlineData(0,  10, 1, 0, false, false)]
    [InlineData(10, 10, 1, 1, false, false)]
    [InlineData(25, 10, 1, 3, true,  false)]
    [InlineData(25, 10, 2, 3, true,  true)]
    [InlineData(25, 10, 3, 3, false, true)]
    public void Create_ComputesPaginationCorrectly(
        int total, int pageSize, int page, int expectedPages,
        bool expectNext, bool expectPrev)
    {
        var result = PagedResult<int>.Create([], total, page, pageSize);
        result.TotalPages.Should().Be(expectedPages);
        result.HasNextPage.Should().Be(expectNext);
        result.HasPreviousPage.Should().Be(expectPrev);
    }
}
