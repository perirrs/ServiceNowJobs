using SNHub.Profiles.IntegrationTests.Brokers;
using Xunit;

namespace SNHub.Profiles.IntegrationTests;

[CollectionDefinition(nameof(ProfilesApiCollection))]
public sealed class ProfilesApiCollection : ICollectionFixture<ProfilesWebApplicationFactory> { }
