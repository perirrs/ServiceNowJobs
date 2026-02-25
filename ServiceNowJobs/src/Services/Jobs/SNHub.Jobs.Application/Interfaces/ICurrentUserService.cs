using System.Security.Claims;

namespace SNHub.Jobs.Application.Interfaces;

/// <summary>
/// Abstracts the current authenticated user away from HttpContext.
/// Allows command handlers and query handlers to access user identity
/// without a direct dependency on ASP.NET Core — keeping Application
/// layer infrastructure-free and fully unit-testable.
/// </summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    IEnumerable<string> Roles { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
}
