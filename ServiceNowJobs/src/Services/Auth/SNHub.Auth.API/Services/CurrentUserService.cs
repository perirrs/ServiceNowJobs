using SNHub.Auth.Application.Interfaces;
using System.Security.Claims;

namespace SNHub.Auth.API.Services;

public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _http;

    public CurrentUserService(IHttpContextAccessor http) => _http = http;

    private ClaimsPrincipal? Principal => _http.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var sub = Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? Principal?.FindFirstValue("sub");
            return Guid.TryParse(sub, out var id) ? id : null;
        }
    }

    public string? Email =>
        Principal?.FindFirstValue(ClaimTypes.Email) ?? Principal?.FindFirstValue("email");

    public IEnumerable<string> Roles =>
        Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value)
        ?? Enumerable.Empty<string>();

    public string? IpAddress
    {
        get
        {
            var forwarded = _http.HttpContext?.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwarded))
                return forwarded.Split(',')[0].Trim();
            return _http.HttpContext?.Connection.RemoteIpAddress?.ToString();
        }
    }

    public string? UserAgent =>
        _http.HttpContext?.Request.Headers["User-Agent"].FirstOrDefault();

    public bool IsAuthenticated =>
        Principal?.Identity?.IsAuthenticated ?? false;
}
