using SNHub.Notifications.IntegrationTests.Models;
using System.Net.Http.Json;

namespace SNHub.Notifications.IntegrationTests.Brokers;

public sealed class NotificationsApiBroker
{
    private readonly HttpClient _client;
    private const string Base = "/api/v1/notifications";

    public NotificationsApiBroker(HttpClient client) => _client = client;

    public Task<HttpResponseMessage> GetMineAsync(bool? unreadOnly = null, int page = 1, int pageSize = 20)
    {
        var qs = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (unreadOnly.HasValue) qs.Add($"unreadOnly={unreadOnly.Value}");
        return _client.GetAsync($"{Base}?{string.Join("&", qs)}");
    }

    public Task<HttpResponseMessage> GetUnreadCountAsync()
        => _client.GetAsync($"{Base}/unread-count");

    public Task<HttpResponseMessage> MarkReadAsync(Guid id)
        => _client.PutAsync($"{Base}/{id}/read", null);

    public Task<HttpResponseMessage> MarkAllReadAsync()
        => _client.PutAsync($"{Base}/read-all", null);

    public Task<HttpResponseMessage> CreateInternalAsync(CreateNotificationRequest req)
        => _client.PostAsJsonAsync($"{Base}/internal", req);
}
