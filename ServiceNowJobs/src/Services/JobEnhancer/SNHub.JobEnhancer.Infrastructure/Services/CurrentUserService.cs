using Microsoft.AspNetCore.Http;
using SNHub.JobEnhancer.Application.Interfaces;
using System.Security.Claims;

namespace SNHub.JobEnhancer.Infrastructure.Services;

public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _http;
    public CurrentUserService(IHttpContextAccessor http) => _http = http;

    public Guid? UserId
    {
        get
        {
            var value = _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? _http.HttpContext?.User.FindFirstValue("sub");
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public bool IsAuthenticated => _http.HttpContext?.User.Identity?.IsAuthenticated == true;
    public bool IsInRole(string role) => _http.HttpContext?.User.IsInRole(role) == true;
}
