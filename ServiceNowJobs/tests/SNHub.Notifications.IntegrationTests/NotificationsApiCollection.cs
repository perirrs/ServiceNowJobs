using SNHub.Notifications.IntegrationTests.Brokers;
using System.Net.Http.Json;
using Xunit;

namespace SNHub.Notifications.IntegrationTests;

[CollectionDefinition(nameof(NotificationsApiCollection))]
public sealed class NotificationsApiCollection
    : ICollectionFixture<NotificationsWebApplicationFactory> { }
