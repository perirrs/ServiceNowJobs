using Xunit;
using SNHub.Auth.IntegrationTests.Brokers;

namespace SNHub.Auth.IntegrationTests;

/// <summary>
/// Ensures all integration tests share a single set of containers.
/// Testcontainers start once per test session, not once per test.
/// </summary>
[CollectionDefinition(nameof(AuthApiCollection))]
public sealed class AuthApiCollection : ICollectionFixture<AuthWebApplicationFactory>;
