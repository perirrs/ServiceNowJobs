namespace SNHub.Users.Domain.Entities;

public sealed class UserProfile
{
    private UserProfile() { }

    public Guid   Id                 { get; private set; }
    public Guid   UserId             { get; private set; }
    public string? FirstName         { get; private set; }
    public string? LastName          { get; private set; }
    public string? Email             { get; private set; }
    public string? PhoneNumber       { get; private set; }
    public string? Headline          { get; private set; }
    public string? Bio               { get; private set; }
    public string? Location          { get; private set; }
    public string? ProfilePictureUrl { get; private set; }
    public string? LinkedInUrl       { get; private set; }
    public string? GitHubUrl         { get; private set; }
    public string? WebsiteUrl        { get; private set; }
    public bool    IsPublic          { get; private set; } = true;
    public int     YearsOfExperience { get; private set; }
    public string? Country           { get; private set; }
    public string? TimeZone          { get; private set; }
    public bool    IsDeleted         { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid?   DeletedBy         { get; private set; }
    public DateTimeOffset CreatedAt  { get; private set; }
    public DateTimeOffset UpdatedAt  { get; private set; }

    public static UserProfile Create(Guid userId) => new()
    {
        Id        = Guid.NewGuid(),
        UserId    = userId,
        IsPublic  = true,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    public void Update(
        string? firstName, string? lastName, string? email, string? phoneNumber,
        string? headline, string? bio, string? location,
        string? linkedInUrl, string? gitHubUrl, string? websiteUrl,
        int yearsOfExperience, bool isPublic, string? country, string? timeZone)
    {
        FirstName         = firstName?.Trim();
        LastName          = lastName?.Trim();
        Email             = email?.Trim().ToLowerInvariant();
        PhoneNumber       = phoneNumber?.Trim();
        Headline          = headline?.Trim();
        Bio               = bio?.Trim();
        Location          = location?.Trim();
        LinkedInUrl       = linkedInUrl?.Trim();
        GitHubUrl         = gitHubUrl?.Trim();
        WebsiteUrl        = websiteUrl?.Trim();
        YearsOfExperience = yearsOfExperience;
        IsPublic          = isPublic;
        Country           = country;
        TimeZone          = timeZone;
        UpdatedAt         = DateTimeOffset.UtcNow;
    }

    public void SetProfilePicture(string url)
    {
        ProfilePictureUrl = url;
        UpdatedAt         = DateTimeOffset.UtcNow;
    }

    public void SoftDelete(Guid deletedBy)
    {
        if (IsDeleted) return;
        IsDeleted  = true;
        DeletedAt  = DateTimeOffset.UtcNow;
        DeletedBy  = deletedBy;
        UpdatedAt  = DateTimeOffset.UtcNow;
    }

    public void Reinstate()
    {
        IsDeleted = false;
        DeletedAt = null;
        DeletedBy = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public string FullName => $"{FirstName} {LastName}".Trim();
}
