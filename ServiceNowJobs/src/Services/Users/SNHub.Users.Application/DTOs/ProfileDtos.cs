namespace SNHub.Users.Application.DTOs;

public sealed record UserProfileDto(
    Guid   Id,
    Guid   UserId,
    string? FirstName,
    string? LastName,
    string? Email,
    string? PhoneNumber,
    string? Headline,
    string? Bio,
    string? Location,
    string? ProfilePictureUrl,
    string? LinkedInUrl,
    string? GitHubUrl,
    string? WebsiteUrl,
    bool   IsPublic,
    int    YearsOfExperience,
    string? Country,
    string? TimeZone,
    bool   IsDeleted,
    DateTimeOffset  CreatedAt,
    DateTimeOffset  UpdatedAt);

public sealed record AdminUserDto(
    Guid   Id,
    Guid   UserId,
    string? FirstName,
    string? LastName,
    string? Email,
    string? PhoneNumber,
    string? Headline,
    string? Location,
    string? Country,
    bool   IsPublic,
    bool   IsDeleted,
    DateTimeOffset?  DeletedAt,
    DateTimeOffset   CreatedAt,
    DateTimeOffset   UpdatedAt);

public sealed record PagedResult<T>(
    IEnumerable<T> Items,
    int Total,
    int Page,
    int PageSize,
    bool HasNextPage,
    bool HasPreviousPage)
{
    public int TotalPages => Total == 0 ? 0 : (int)Math.Ceiling((double)Total / PageSize);

    public static PagedResult<T> Create(IEnumerable<T> items, int total, int page, int pageSize)
        => new(items, total, page, pageSize,
            HasNextPage: page < (int)Math.Ceiling((double)total / pageSize),
            HasPreviousPage: page > 1);
}

public sealed record UploadedFileResponse(string Url);
