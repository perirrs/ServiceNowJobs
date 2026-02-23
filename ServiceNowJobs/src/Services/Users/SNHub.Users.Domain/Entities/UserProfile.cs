namespace SNHub.Users.Domain.Entities;

public sealed class UserProfile
{
    private UserProfile() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string? Headline { get; private set; }
    public string? Bio { get; private set; }
    public string? Location { get; private set; }
    public string? ProfilePictureUrl { get; private set; }
    public string? CvUrl { get; private set; }
    public string? LinkedInUrl { get; private set; }
    public string? GitHubUrl { get; private set; }
    public string? WebsiteUrl { get; private set; }
    public bool IsPublic { get; private set; } = true;
    public int YearsOfExperience { get; private set; }
    public string? Country { get; private set; }
    public string? TimeZone { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static UserProfile Create(Guid userId) => new()
    {
        Id        = Guid.NewGuid(),
        UserId    = userId,
        IsPublic  = true,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    public void Update(string? headline, string? bio, string? location,
        string? linkedInUrl, string? gitHubUrl, string? websiteUrl,
        int yearsOfExperience, bool isPublic, string? country, string? timeZone)
    {
        Headline           = headline?.Trim();
        Bio                = bio?.Trim();
        Location           = location?.Trim();
        LinkedInUrl        = linkedInUrl?.Trim();
        GitHubUrl          = gitHubUrl?.Trim();
        WebsiteUrl         = websiteUrl?.Trim();
        YearsOfExperience  = yearsOfExperience;
        IsPublic           = isPublic;
        Country            = country;
        TimeZone           = timeZone;
        UpdatedAt          = DateTimeOffset.UtcNow;
    }

    public void SetProfilePicture(string url) { ProfilePictureUrl = url; UpdatedAt = DateTimeOffset.UtcNow; }
    public void SetCvUrl(string url) { CvUrl = url; UpdatedAt = DateTimeOffset.UtcNow; }
}
