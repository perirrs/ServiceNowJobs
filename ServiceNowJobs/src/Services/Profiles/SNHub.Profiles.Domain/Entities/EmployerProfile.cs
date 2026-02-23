namespace SNHub.Profiles.Domain.Entities;

public sealed class EmployerProfile
{
    private EmployerProfile() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string? CompanyName { get; private set; }
    public string? CompanyDescription { get; private set; }
    public string? Industry { get; private set; }
    public string? CompanySize { get; private set; }   // "1-10", "11-50", "51-200", "201-500", "500+"
    public string? HeadquartersCity { get; private set; }
    public string? HeadquartersCountry { get; private set; }
    public string? WebsiteUrl { get; private set; }
    public string? LinkedInUrl { get; private set; }
    public string? LogoUrl { get; private set; }
    public bool IsVerified { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static EmployerProfile Create(Guid userId) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, IsVerified = false,
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    };

    public void Update(string? companyName, string? description, string? industry,
        string? size, string? city, string? country, string? website, string? linkedIn)
    {
        CompanyName = companyName?.Trim(); CompanyDescription = description?.Trim();
        Industry = industry?.Trim(); CompanySize = size;
        HeadquartersCity = city?.Trim(); HeadquartersCountry = country;
        WebsiteUrl = website?.Trim(); LinkedInUrl = linkedIn?.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetLogo(string url) { LogoUrl = url; UpdatedAt = DateTimeOffset.UtcNow; }
    public void Verify() { IsVerified = true; UpdatedAt = DateTimeOffset.UtcNow; }
}
