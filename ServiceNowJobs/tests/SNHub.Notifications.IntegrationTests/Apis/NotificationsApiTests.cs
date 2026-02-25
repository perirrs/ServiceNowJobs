using FluentAssertions;
using SNHub.Notifications.IntegrationTests.Brokers;
using SNHub.Notifications.IntegrationTests.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace SNHub.Notifications.IntegrationTests.Apis;

[Collection(nameof(NotificationsApiCollection))]
public sealed partial class NotificationsApiTests
{
    private readonly NotificationsWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    private static readonly Guid _user1Id = Guid.NewGuid();
    private static readonly Guid _user2Id = Guid.NewGuid();
    private static readonly Guid _adminId = Guid.NewGuid();

    private static readonly string _user1Token = NotificationsWebApplicationFactory.GenerateToken(_user1Id, "Candidate");
    private static readonly string _user2Token = NotificationsWebApplicationFactory.GenerateToken(_user2Id, "Candidate");
    private static readonly string _adminToken = NotificationsWebApplicationFactory.GenerateToken(_adminId, "SuperAdmin");

    public NotificationsApiTests(NotificationsWebApplicationFactory factory) => _factory = factory;

    private NotificationsApiBroker BrokerFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new NotificationsApiBroker(client);
    }

    private NotificationsApiBroker AnonBroker() => new(_factory.CreateClient());

    // Helper: create a notification via internal endpoint as admin
    private async Task<NotificationResponse> CreateNotificationAsync(
        Guid userId, string title = "Test notification", string message = "Test message",
        int type = 1, string? actionUrl = null)
    {
        var req = new CreateNotificationRequest
        {
            UserId = userId, Type = type, Title = title, Message = message, ActionUrl = actionUrl
        };
        var response = await BrokerFor(_adminToken).CreateInternalAsync(req);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<NotificationResponse>(_json))!;
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Create (Internal endpoint)
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class NotificationsApiTests
{
    [Fact]
    public async Task Create_SuperAdmin_Returns201WithNotification()
    {
        await _factory.ResetDatabaseAsync();
        var req = new CreateNotificationRequest
        {
            UserId  = _user1Id, Type = 1,
            Title   = "Job match", Message = "A new role matches you.",
            ActionUrl = "https://snhub.io/jobs/abc"
        };
        var response = await BrokerFor(_adminToken).CreateInternalAsync(req);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<NotificationResponse>(_json);
        body!.UserId.Should().Be(_user1Id);
        body.Title.Should().Be("Job match");
        body.IsRead.Should().BeFalse();
        body.ActionUrl.Should().Be("https://snhub.io/jobs/abc");
    }

    [Fact]
    public async Task Create_CandidateRole_Returns403()
    {
        var req = new CreateNotificationRequest
        {
            UserId = _user1Id, Type = 1, Title = "T", Message = "M"
        };
        var response = await BrokerFor(_user1Token).CreateInternalAsync(req);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_NoAuth_Returns401()
    {
        var req = new CreateNotificationRequest
        {
            UserId = _user1Id, Type = 1, Title = "T", Message = "M"
        };
        var response = await AnonBroker().CreateInternalAsync(req);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_EmptyTitle_Returns400()
    {
        var req = new CreateNotificationRequest
        {
            UserId = _user1Id, Type = 1, Title = "", Message = "Valid message"
        };
        var response = await BrokerFor(_adminToken).CreateInternalAsync(req);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_InvalidActionUrl_Returns400()
    {
        var req = new CreateNotificationRequest
        {
            UserId    = _user1Id, Type = 1,
            Title     = "Title", Message = "Message",
            ActionUrl = "not-a-valid-url"
        };
        var response = await BrokerFor(_adminToken).CreateInternalAsync(req);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Get My Notifications
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class NotificationsApiTests
{
    [Fact]
    public async Task GetMine_NoNotifications_ReturnsEmptyPaged()
    {
        await _factory.ResetDatabaseAsync();
        var response = await BrokerFor(_user1Token).GetMineAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedNotificationResponse>(_json);
        body!.Total.Should().Be(0);
        body.Items.Should().BeEmpty();
        body.UnreadCount.Should().Be(0);
    }

    [Fact]
    public async Task GetMine_ReturnsOnlyOwnNotifications()
    {
        await _factory.ResetDatabaseAsync();
        await CreateNotificationAsync(_user1Id, "User1 notification");
        await CreateNotificationAsync(_user2Id, "User2 notification");

        var response = await BrokerFor(_user1Token).GetMineAsync();
        var body     = await response.Content.ReadFromJsonAsync<PagedNotificationResponse>(_json);

        body!.Total.Should().Be(1);
        body.Items[0].Title.Should().Be("User1 notification");
    }

    [Fact]
    public async Task GetMine_ReturnsNewestFirst()
    {
        await _factory.ResetDatabaseAsync();
        await CreateNotificationAsync(_user1Id, "First");
        await Task.Delay(10); // ensure ordering
        await CreateNotificationAsync(_user1Id, "Second");

        var response = await BrokerFor(_user1Token).GetMineAsync();
        var body     = await response.Content.ReadFromJsonAsync<PagedNotificationResponse>(_json);

        body!.Items[0].Title.Should().Be("Second"); // newest first
        body.Items[1].Title.Should().Be("First");
    }

    [Fact]
    public async Task GetMine_UnreadOnlyFilter_ReturnsOnlyUnread()
    {
        await _factory.ResetDatabaseAsync();
        var n1 = await CreateNotificationAsync(_user1Id, "Unread one");
        var n2 = await CreateNotificationAsync(_user1Id, "Unread two");

        // mark one as read
        await BrokerFor(_user1Token).MarkReadAsync(n1.Id);

        var response = await BrokerFor(_user1Token).GetMineAsync(unreadOnly: true);
        var body     = await response.Content.ReadFromJsonAsync<PagedNotificationResponse>(_json);

        body!.Total.Should().Be(1);
        body.Items[0].Title.Should().Be("Unread two");
    }

    [Fact]
    public async Task GetMine_UnreadCount_ReflectsActualUnread()
    {
        await _factory.ResetDatabaseAsync();
        await CreateNotificationAsync(_user1Id, "N1");
        await CreateNotificationAsync(_user1Id, "N2");
        await CreateNotificationAsync(_user1Id, "N3");

        var response = await BrokerFor(_user1Token).GetMineAsync();
        var body     = await response.Content.ReadFromJsonAsync<PagedNotificationResponse>(_json);

        body!.UnreadCount.Should().Be(3);
        body.Total.Should().Be(3);
    }

    [Fact]
    public async Task GetMine_Pagination_Works()
    {
        await _factory.ResetDatabaseAsync();
        for (int i = 1; i <= 7; i++)
            await CreateNotificationAsync(_user1Id, $"Notification {i}");

        var page1 = await BrokerFor(_user1Token).GetMineAsync(page: 1, pageSize: 5);
        var body1 = await page1.Content.ReadFromJsonAsync<PagedNotificationResponse>(_json);
        body1!.Items.Should().HaveCount(5);
        body1.HasNextPage.Should().BeTrue();
        body1.HasPreviousPage.Should().BeFalse();

        var page2 = await BrokerFor(_user1Token).GetMineAsync(page: 2, pageSize: 5);
        var body2 = await page2.Content.ReadFromJsonAsync<PagedNotificationResponse>(_json);
        body2!.Items.Should().HaveCount(2);
        body2.HasNextPage.Should().BeFalse();
        body2.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public async Task GetMine_NoAuth_Returns401()
    {
        var response = await AnonBroker().GetMineAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Unread Count
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class NotificationsApiTests
{
    [Fact]
    public async Task GetUnreadCount_NoNotifications_ReturnsZero()
    {
        await _factory.ResetDatabaseAsync();
        var response = await BrokerFor(_user1Token).GetUnreadCountAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UnreadCountResponse>(_json);
        body!.Count.Should().Be(0);
    }

    [Fact]
    public async Task GetUnreadCount_AfterMarkAllRead_ReturnsZero()
    {
        await _factory.ResetDatabaseAsync();
        await CreateNotificationAsync(_user1Id, "N1");
        await CreateNotificationAsync(_user1Id, "N2");
        await BrokerFor(_user1Token).MarkAllReadAsync();

        var response = await BrokerFor(_user1Token).GetUnreadCountAsync();
        var body     = await response.Content.ReadFromJsonAsync<UnreadCountResponse>(_json);
        body!.Count.Should().Be(0);
    }

    [Fact]
    public async Task GetUnreadCount_NoAuth_Returns401()
    {
        var response = await AnonBroker().GetUnreadCountAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Mark Single Notification Read
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class NotificationsApiTests
{
    [Fact]
    public async Task MarkRead_OwnNotification_Returns204()
    {
        await _factory.ResetDatabaseAsync();
        var n = await CreateNotificationAsync(_user1Id, "Unread notification");

        var response = await BrokerFor(_user1Token).MarkReadAsync(n.Id);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task MarkRead_NotificationIsMarkedRead()
    {
        await _factory.ResetDatabaseAsync();
        var n = await CreateNotificationAsync(_user1Id, "Unread notification");
        await BrokerFor(_user1Token).MarkReadAsync(n.Id);

        var list = await BrokerFor(_user1Token).GetMineAsync(unreadOnly: true);
        var body = await list.Content.ReadFromJsonAsync<PagedNotificationResponse>(_json);
        body!.Total.Should().Be(0); // no more unread
    }

    [Fact]
    public async Task MarkRead_OtherUsersNotification_Returns403()
    {
        await _factory.ResetDatabaseAsync();
        var n = await CreateNotificationAsync(_user1Id, "User1's notification");

        // user2 tries to mark user1's notification as read
        var response = await BrokerFor(_user2Token).MarkReadAsync(n.Id);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MarkRead_NonExistentId_Returns404()
    {
        var response = await BrokerFor(_user1Token).MarkReadAsync(Guid.NewGuid());
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MarkRead_AlreadyRead_Returns204Idempotent()
    {
        await _factory.ResetDatabaseAsync();
        var n = await CreateNotificationAsync(_user1Id, "Already read");
        await BrokerFor(_user1Token).MarkReadAsync(n.Id); // first mark

        var response = await BrokerFor(_user1Token).MarkReadAsync(n.Id); // second mark
        response.StatusCode.Should().Be(HttpStatusCode.NoContent); // still 204
    }

    [Fact]
    public async Task MarkRead_NoAuth_Returns401()
    {
        var response = await AnonBroker().MarkReadAsync(Guid.NewGuid());
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Mark All Read
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class NotificationsApiTests
{
    [Fact]
    public async Task MarkAllRead_Returns204()
    {
        await _factory.ResetDatabaseAsync();
        await CreateNotificationAsync(_user1Id, "N1");
        await CreateNotificationAsync(_user1Id, "N2");

        var response = await BrokerFor(_user1Token).MarkAllReadAsync();
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task MarkAllRead_AllNotificationsBecomesRead()
    {
        await _factory.ResetDatabaseAsync();
        await CreateNotificationAsync(_user1Id, "N1");
        await CreateNotificationAsync(_user1Id, "N2");
        await CreateNotificationAsync(_user1Id, "N3");

        await BrokerFor(_user1Token).MarkAllReadAsync();

        var list = await BrokerFor(_user1Token).GetMineAsync(unreadOnly: true);
        var body = await list.Content.ReadFromJsonAsync<PagedNotificationResponse>(_json);
        body!.Total.Should().Be(0);
    }

    [Fact]
    public async Task MarkAllRead_DoesNotAffectOtherUsers()
    {
        await _factory.ResetDatabaseAsync();
        await CreateNotificationAsync(_user1Id, "User1 notification");
        await CreateNotificationAsync(_user2Id, "User2 notification");

        await BrokerFor(_user1Token).MarkAllReadAsync(); // only user1 marks all read

        var user2List = await BrokerFor(_user2Token).GetMineAsync(unreadOnly: true);
        var body      = await user2List.Content.ReadFromJsonAsync<PagedNotificationResponse>(_json);
        body!.Total.Should().Be(1); // user2's notification still unread
    }

    [Fact]
    public async Task MarkAllRead_NoNotifications_Returns204()
    {
        await _factory.ResetDatabaseAsync();
        var response = await BrokerFor(_user1Token).MarkAllReadAsync();
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task MarkAllRead_NoAuth_Returns401()
    {
        var response = await AnonBroker().MarkAllReadAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Health
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class NotificationsApiTests
{
    [Fact]
    public async Task Health_Returns200()
    {
        var response = await _factory.CreateClient().GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
