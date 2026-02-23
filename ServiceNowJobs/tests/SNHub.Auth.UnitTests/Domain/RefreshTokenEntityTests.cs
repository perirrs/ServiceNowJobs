using FluentAssertions;
using SNHub.Auth.Domain.Entities;
using Xunit;

namespace SNHub.Auth.UnitTests.Domain;

public sealed class RefreshTokenEntityTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    // ── Factory ───────────────────────────────────────────────────────────────

    [Fact]
    public void Create_ValidInputs_SetsAllProperties()
    {
        var expiry = DateTimeOffset.UtcNow.AddDays(30);
        var token = RefreshToken.Create(UserId, "tok123", "1.2.3.4", "Mozilla/5.0", expiry);

        token.Id.Should().NotBeEmpty();
        token.UserId.Should().Be(UserId);
        token.Token.Should().Be("tok123");
        token.CreatedByIp.Should().Be("1.2.3.4");
        token.UserAgent.Should().Be("Mozilla/5.0");
        token.ExpiresAt.Should().Be(expiry);
        token.IsRevoked.Should().BeFalse();
        token.IsExpired.Should().BeFalse();
        token.IsActive.Should().BeTrue();
        token.RevokedAt.Should().BeNull();
        token.RevokeReason.Should().BeNull();
    }

    [Fact]
    public void Create_EachCall_ProducesUniqueId()
    {
        var t1 = RefreshToken.Create(UserId, "a", "ip", "ua", DateTimeOffset.UtcNow.AddDays(1));
        var t2 = RefreshToken.Create(UserId, "b", "ip", "ua", DateTimeOffset.UtcNow.AddDays(1));

        t1.Id.Should().NotBe(t2.Id);
    }

    // ── IsExpired ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsExpired_FutureExpiry_ReturnsFalse()
    {
        var token = RefreshToken.Create(UserId, "t", "ip", "ua", DateTimeOffset.UtcNow.AddHours(1));
        token.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void IsExpired_PastExpiry_ReturnsTrue()
    {
        var token = RefreshToken.Create(UserId, "t", "ip", "ua", DateTimeOffset.UtcNow.AddHours(-1));
        token.IsExpired.Should().BeTrue();
    }

    // ── IsRevoked ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsRevoked_BeforeRevoke_ReturnsFalse()
    {
        var token = RefreshToken.Create(UserId, "t", "ip", "ua", DateTimeOffset.UtcNow.AddDays(1));
        token.IsRevoked.Should().BeFalse();
    }

    [Fact]
    public void IsRevoked_AfterRevoke_ReturnsTrue()
    {
        var token = RefreshToken.Create(UserId, "t", "ip", "ua", DateTimeOffset.UtcNow.AddDays(1));
        token.Revoke("test", "2.3.4.5");
        token.IsRevoked.Should().BeTrue();
    }

    // ── IsActive ──────────────────────────────────────────────────────────────

    [Fact]
    public void IsActive_ValidToken_ReturnsTrue()
    {
        var token = RefreshToken.Create(UserId, "t", "ip", "ua", DateTimeOffset.UtcNow.AddDays(1));
        token.IsActive.Should().BeTrue();
    }

    [Fact]
    public void IsActive_ExpiredToken_ReturnsFalse()
    {
        var token = RefreshToken.Create(UserId, "t", "ip", "ua", DateTimeOffset.UtcNow.AddHours(-1));
        token.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_RevokedToken_ReturnsFalse()
    {
        var token = RefreshToken.Create(UserId, "t", "ip", "ua", DateTimeOffset.UtcNow.AddDays(1));
        token.Revoke("logout", "ip");
        token.IsActive.Should().BeFalse();
    }

    // ── Revoke ────────────────────────────────────────────────────────────────

    [Fact]
    public void Revoke_SetsAllRevokeFields()
    {
        var before = DateTimeOffset.UtcNow;
        var token = RefreshToken.Create(UserId, "t", "ip", "ua", DateTimeOffset.UtcNow.AddDays(1));

        token.Revoke("Logout", "5.6.7.8", "new_token");

        token.RevokedAt.Should().BeOnOrAfter(before);
        token.RevokedByIp.Should().Be("5.6.7.8");
        token.RevokeReason.Should().Be("Logout");
        token.ReplacedByToken.Should().Be("new_token");
    }

    [Fact]
    public void Revoke_WithoutReplacement_LeavesReplacedByTokenNull()
    {
        var token = RefreshToken.Create(UserId, "t", "ip", "ua", DateTimeOffset.UtcNow.AddDays(1));
        token.Revoke("revoked", "ip");
        token.ReplacedByToken.Should().BeNull();
    }
}
