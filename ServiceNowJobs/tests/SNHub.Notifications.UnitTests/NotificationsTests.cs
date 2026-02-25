using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SNHub.Notifications.Application.Commands.CreateNotification;
using SNHub.Notifications.Application.Commands.MarkAsRead;
using SNHub.Notifications.Application.DTOs;
using SNHub.Notifications.Application.Interfaces;
using SNHub.Notifications.Application.Queries.GetNotifications;
using SNHub.Notifications.Domain.Entities;
using SNHub.Notifications.Domain.Enums;
using SNHub.Notifications.Domain.Exceptions;
using Xunit;

namespace SNHub.Notifications.UnitTests;

// ════════════════════════════════════════════════════════════════════════════════
// Domain Entity — Notification
// ════════════════════════════════════════════════════════════════════════════════

public sealed class NotificationEntityTests
{
    [Fact]
    public void Create_ValidData_SetsAllFields()
    {
        var userId = Guid.NewGuid();
        var n = Notification.Create(userId, NotificationType.JobMatch,
            "New job match", "A new ServiceNow role matches your profile.",
            "https://snhub.io/jobs/123", "{\"jobId\":\"123\"}");

        n.Id.Should().NotBeEmpty();
        n.UserId.Should().Be(userId);
        n.Type.Should().Be(NotificationType.JobMatch);
        n.Title.Should().Be("New job match");
        n.Message.Should().Be("A new ServiceNow role matches your profile.");
        n.ActionUrl.Should().Be("https://snhub.io/jobs/123");
        n.MetadataJson.Should().Be("{\"jobId\":\"123\"}");
        n.IsRead.Should().BeFalse();
        n.ReadAt.Should().BeNull();
        n.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Create_WithoutOptionals_Succeeds()
    {
        var n = Notification.Create(Guid.NewGuid(), NotificationType.SystemAlert,
            "System maintenance", "Scheduled downtime in 2 hours.");

        n.ActionUrl.Should().BeNull();
        n.MetadataJson.Should().BeNull();
        n.IsRead.Should().BeFalse();
    }

    [Fact]
    public void MarkAsRead_SetsIsReadAndReadAt()
    {
        var n = Notification.Create(Guid.NewGuid(), NotificationType.NewMessage,
            "New message", "You have a new message.");

        n.MarkAsRead();

        n.IsRead.Should().BeTrue();
        n.ReadAt.Should().NotBeNull();
        n.ReadAt!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void MarkAsRead_CalledTwice_IsIdempotent()
    {
        var n = Notification.Create(Guid.NewGuid(), NotificationType.NewMessage,
            "New message", "You have a new message.");

        n.MarkAsRead();
        var firstReadAt = n.ReadAt;
        n.MarkAsRead();

        n.IsRead.Should().BeTrue();
        // ReadAt should not change on second call (idempotent)
        n.ReadAt.Should().Be(firstReadAt);
    }

    [Fact]
    public void Create_AllNotificationTypes_AreValid()
    {
        foreach (NotificationType type in Enum.GetValues<NotificationType>())
        {
            var n = Notification.Create(Guid.NewGuid(), type, "Title", "Message");
            n.Type.Should().Be(type);
        }
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// NotificationMapper
// ════════════════════════════════════════════════════════════════════════════════

public sealed class NotificationMapperTests
{
    [Fact]
    public void ToDto_UnreadNotification_MapsAllFields()
    {
        var n = Notification.Create(Guid.NewGuid(), NotificationType.ApplicationStatusChanged,
            "Application update", "Your application status changed.",
            "https://snhub.io/applications/456");

        var dto = NotificationMapper.ToDto(n);

        dto.Id.Should().Be(n.Id);
        dto.UserId.Should().Be(n.UserId);
        dto.Type.Should().Be("ApplicationStatusChanged");
        dto.Title.Should().Be("Application update");
        dto.IsRead.Should().BeFalse();
        dto.ReadAt.Should().BeNull();
        dto.ActionUrl.Should().Be("https://snhub.io/applications/456");
    }

    [Fact]
    public void ToDto_ReadNotification_HasReadAt()
    {
        var n = Notification.Create(Guid.NewGuid(), NotificationType.ProfileView,
            "Profile viewed", "Someone viewed your profile.");
        n.MarkAsRead();

        var dto = NotificationMapper.ToDto(n);

        dto.IsRead.Should().BeTrue();
        dto.ReadAt.Should().NotBeNull();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// CreateNotification — Validator
// ════════════════════════════════════════════════════════════════════════════════

public sealed class CreateNotificationCommandValidatorTests
{
    private readonly CreateNotificationCommandValidator _sut = new();

    private static CreateNotificationCommand Valid() => new(
        Guid.NewGuid(), NotificationType.JobMatch,
        "New job match", "A ServiceNow role matches your profile.",
        "https://snhub.io/jobs/123");

    [Fact]
    public void Validate_ValidCommand_Passes()
        => _sut.Validate(Valid()).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_EmptyUserId_Fails()
        => _sut.Validate(Valid() with { UserId = Guid.Empty }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_EmptyTitle_Fails()
        => _sut.Validate(Valid() with { Title = "" }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_TitleTooLong_Fails()
        => _sut.Validate(Valid() with { Title = new string('x', 201) }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_EmptyMessage_Fails()
        => _sut.Validate(Valid() with { Message = "" }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_MessageTooLong_Fails()
        => _sut.Validate(Valid() with { Message = new string('x', 1_001) }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_InvalidActionUrl_Fails()
        => _sut.Validate(Valid() with { ActionUrl = "not-a-url" }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_NullActionUrl_Passes()
        => _sut.Validate(Valid() with { ActionUrl = null }).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_InvalidEnumType_Fails()
        => _sut.Validate(Valid() with { Type = (NotificationType)999 }).IsValid.Should().BeFalse();
}

// ════════════════════════════════════════════════════════════════════════════════
// MarkNotificationRead — Validator
// ════════════════════════════════════════════════════════════════════════════════

public sealed class MarkNotificationReadCommandValidatorTests
{
    private readonly MarkNotificationReadCommandValidator _sut = new();

    [Fact]
    public void Validate_ValidCommand_Passes()
        => _sut.Validate(new MarkNotificationReadCommand(Guid.NewGuid(), Guid.NewGuid()))
               .IsValid.Should().BeTrue();

    [Fact]
    public void Validate_EmptyId_Fails()
        => _sut.Validate(new MarkNotificationReadCommand(Guid.Empty, Guid.NewGuid()))
               .IsValid.Should().BeFalse();

    [Fact]
    public void Validate_EmptyRequesterId_Fails()
        => _sut.Validate(new MarkNotificationReadCommand(Guid.NewGuid(), Guid.Empty))
               .IsValid.Should().BeFalse();
}

// ════════════════════════════════════════════════════════════════════════════════
// GetNotifications — Validator
// ════════════════════════════════════════════════════════════════════════════════

public sealed class GetNotificationsQueryValidatorTests
{
    private readonly GetNotificationsQueryValidator _sut = new();

    [Fact]
    public void Validate_DefaultParams_Passes()
        => _sut.Validate(new GetNotificationsQuery(Guid.NewGuid(), null)).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_EmptyUserId_Fails()
        => _sut.Validate(new GetNotificationsQuery(Guid.Empty, null)).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_ZeroPage_Fails()
        => _sut.Validate(new GetNotificationsQuery(Guid.NewGuid(), null, Page: 0)).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_PageSizeOver100_Fails()
        => _sut.Validate(new GetNotificationsQuery(Guid.NewGuid(), null, PageSize: 101)).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_PageSize100_Passes()
        => _sut.Validate(new GetNotificationsQuery(Guid.NewGuid(), null, PageSize: 100)).IsValid.Should().BeTrue();
}

// ════════════════════════════════════════════════════════════════════════════════
// CreateNotification — Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class CreateNotificationCommandHandlerTests
{
    private readonly Mock<INotificationRepository> _repo = new();
    private readonly Mock<IUnitOfWork>             _uow  = new();

    private CreateNotificationCommandHandler Handler() =>
        new(_repo.Object, _uow.Object, NullLogger<CreateNotificationCommandHandler>.Instance);

    [Fact]
    public async Task Handle_ValidCommand_CreatesAndSaves()
    {
        var userId = Guid.NewGuid();
        var dto = await Handler().Handle(
            new CreateNotificationCommand(userId, NotificationType.JobMatch,
                "New match", "A role matches your profile."),
            CancellationToken.None);

        dto.UserId.Should().Be(userId);
        dto.Type.Should().Be("JobMatch");
        dto.IsRead.Should().BeFalse();
        _repo.Verify(r => r.AddAsync(It.IsAny<Notification>(), default), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_WithActionUrl_UrlPreservedInDto()
    {
        var dto = await Handler().Handle(
            new CreateNotificationCommand(Guid.NewGuid(), NotificationType.ApplicationStatusChanged,
                "Status update", "Your application moved to Interview.",
                "https://snhub.io/applications/789"),
            CancellationToken.None);

        dto.ActionUrl.Should().Be("https://snhub.io/applications/789");
    }

    [Fact]
    public async Task Handle_AllNotificationTypes_Succeed()
    {
        foreach (NotificationType type in Enum.GetValues<NotificationType>())
        {
            var dto = await Handler().Handle(
                new CreateNotificationCommand(Guid.NewGuid(), type, "Title", "Message"),
                CancellationToken.None);
            dto.Type.Should().Be(type.ToString());
        }
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// MarkNotificationRead — Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class MarkNotificationReadCommandHandlerTests
{
    private readonly Mock<INotificationRepository> _repo = new();
    private readonly Mock<IUnitOfWork>             _uow  = new();

    private MarkNotificationReadCommandHandler Handler() =>
        new(_repo.Object, _uow.Object);

    [Fact]
    public async Task Handle_OwnNotification_MarksAsRead()
    {
        var userId = Guid.NewGuid();
        var n = Notification.Create(userId, NotificationType.JobMatch, "Title", "Message");
        _repo.Setup(r => r.GetByIdAsync(n.Id, default)).ReturnsAsync(n);

        await Handler().Handle(new MarkNotificationReadCommand(n.Id, userId), CancellationToken.None);

        n.IsRead.Should().BeTrue();
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_OtherUserNotification_ThrowsAccessDeniedException()
    {
        var n = Notification.Create(Guid.NewGuid(), NotificationType.JobMatch, "Title", "Message");
        _repo.Setup(r => r.GetByIdAsync(n.Id, default)).ReturnsAsync(n);

        var act = async () => await Handler().Handle(
            new MarkNotificationReadCommand(n.Id, Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotificationAccessDeniedException>();
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task Handle_NotFound_ThrowsNotFoundException()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
             .ReturnsAsync((Notification?)null);

        var act = async () => await Handler().Handle(
            new MarkNotificationReadCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotificationNotFoundException>();
    }

    [Fact]
    public async Task Handle_AlreadyRead_DoesNotSaveAgain()
    {
        var userId = Guid.NewGuid();
        var n = Notification.Create(userId, NotificationType.JobMatch, "Title", "Message");
        n.MarkAsRead();
        _repo.Setup(r => r.GetByIdAsync(n.Id, default)).ReturnsAsync(n);

        await Handler().Handle(new MarkNotificationReadCommand(n.Id, userId), CancellationToken.None);

        // already read — SaveChanges should NOT be called again
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// MarkAllRead — Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class MarkAllReadCommandHandlerTests
{
    private readonly Mock<INotificationRepository> _repo = new();
    private readonly Mock<IUnitOfWork>             _uow  = new();

    private MarkAllReadCommandHandler Handler() => new(_repo.Object, _uow.Object);

    [Fact]
    public async Task Handle_CallsMarkAllAsReadAndSaves()
    {
        var userId = Guid.NewGuid();

        await Handler().Handle(new MarkAllReadCommand(userId), CancellationToken.None);

        _repo.Verify(r => r.MarkAllAsReadAsync(userId, default), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// GetNotifications — Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class GetNotificationsQueryHandlerTests
{
    private readonly Mock<INotificationRepository> _repo = new();
    private GetNotificationsQueryHandler Handler() => new(_repo.Object);

    [Fact]
    public async Task Handle_ReturnsPagedResultWithUnreadCount()
    {
        var userId = Guid.NewGuid();
        var notifications = new[]
        {
            Notification.Create(userId, NotificationType.JobMatch, "Match 1", "Message 1"),
            Notification.Create(userId, NotificationType.SystemAlert, "Alert", "Message 2"),
        };
        _repo.Setup(r => r.GetByUserAsync(userId, null, 1, 20, default))
             .ReturnsAsync((notifications, 2, 2));

        var result = await Handler().Handle(
            new GetNotificationsQuery(userId, null), CancellationToken.None);

        result.Total.Should().Be(2);
        result.UnreadCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NoNotifications_ReturnsEmptyResult()
    {
        _repo.Setup(r => r.GetByUserAsync(It.IsAny<Guid>(), null, 1, 20, default))
             .ReturnsAsync((Enumerable.Empty<Notification>(), 0, 0));

        var result = await Handler().Handle(
            new GetNotificationsQuery(Guid.NewGuid(), null), CancellationToken.None);

        result.Total.Should().Be(0);
        result.UnreadCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UnreadOnlyFilter_PassedToRepository()
    {
        var userId = Guid.NewGuid();
        _repo.Setup(r => r.GetByUserAsync(userId, true, 1, 20, default))
             .ReturnsAsync((Enumerable.Empty<Notification>(), 0, 0));

        await Handler().Handle(new GetNotificationsQuery(userId, true), CancellationToken.None);

        _repo.Verify(r => r.GetByUserAsync(userId, true, 1, 20, default), Times.Once);
    }

    [Fact]
    public async Task Handle_Pagination_HasNextPageTrue()
    {
        var userId = Guid.NewGuid();
        _repo.Setup(r => r.GetByUserAsync(userId, null, 1, 5, default))
             .ReturnsAsync((Enumerable.Empty<Notification>(), 12, 3));

        var result = await Handler().Handle(
            new GetNotificationsQuery(userId, null, Page: 1, PageSize: 5), CancellationToken.None);

        result.TotalPages.Should().Be(3);
        result.HasNextPage.Should().BeTrue();
        result.HasPreviousPage.Should().BeFalse();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// GetUnreadCount — Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class GetUnreadCountQueryHandlerTests
{
    private readonly Mock<INotificationRepository> _repo = new();
    private GetUnreadCountQueryHandler Handler() => new(_repo.Object);

    [Fact]
    public async Task Handle_ReturnsCount()
    {
        var userId = Guid.NewGuid();
        _repo.Setup(r => r.GetUnreadCountAsync(userId, default)).ReturnsAsync(7);

        var count = await Handler().Handle(new GetUnreadCountQuery(userId), CancellationToken.None);

        count.Should().Be(7);
    }

    [Fact]
    public async Task Handle_NoUnread_ReturnsZero()
    {
        _repo.Setup(r => r.GetUnreadCountAsync(It.IsAny<Guid>(), default)).ReturnsAsync(0);

        var count = await Handler().Handle(
            new GetUnreadCountQuery(Guid.NewGuid()), CancellationToken.None);

        count.Should().Be(0);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// PagedResult factory
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
        var result = PagedResult<int>.Create([], total, page, pageSize, unreadCount: 0);
        result.TotalPages.Should().Be(expectedPages);
        result.HasNextPage.Should().Be(expectNext);
        result.HasPreviousPage.Should().Be(expectPrev);
    }
}
