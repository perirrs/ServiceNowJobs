namespace SNHub.Users.IntegrationTests.Models;

public sealed class UserProfileResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Headline { get; set; }
    public string? Bio { get; set; }
    public string? Location { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? GitHubUrl { get; set; }
    public string? WebsiteUrl { get; set; }
    public bool IsPublic { get; set; }
    public int YearsOfExperience { get; set; }
    public string? Country { get; set; }
    public string? TimeZone { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AdminUserResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Headline { get; set; }
    public string? Location { get; set; }
    public string? Country { get; set; }
    public bool IsPublic { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PagedUserResponse
{
    public List<AdminUserResponse> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
    public int TotalPages { get; set; }
}

public sealed class UploadedFileResponse
{
    public string Url { get; set; } = string.Empty;
}

public sealed record UpdateProfileRequest(
    string? FirstName         = null,
    string? LastName          = null,
    string? PhoneNumber       = null,
    string? Headline          = null,
    string? Bio               = null,
    string? Location          = null,
    string? LinkedInUrl       = null,
    string? GitHubUrl         = null,
    string? WebsiteUrl        = null,
    int     YearsOfExperience = 0,
    bool    IsPublic          = true,
    string? Country           = null,
    string? TimeZone          = null);
