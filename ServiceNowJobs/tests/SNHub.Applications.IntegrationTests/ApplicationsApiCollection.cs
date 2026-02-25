using SNHub.Applications.IntegrationTests.Brokers;
using Xunit;

namespace SNHub.Applications.IntegrationTests;

[CollectionDefinition(nameof(ApplicationsApiCollection))]
public sealed class ApplicationsApiCollection : ICollectionFixture<ApplicationsWebApplicationFactory> { }
