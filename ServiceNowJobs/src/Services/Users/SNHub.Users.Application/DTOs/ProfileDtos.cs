namespace SNHub.Users.Application.DTOs;

public sealed record UserProfileDto(
    Guid Id, Guid UserId,
    string? Headline, string? Bio, string? Location,
    string? ProfilePictureUrl, string? CvUrl,
    string? LinkedInUrl, string? GitHubUrl, string? WebsiteUrl,
    bool IsPublic, int YearsOfExperience,
    string? Country, string? TimeZone,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
