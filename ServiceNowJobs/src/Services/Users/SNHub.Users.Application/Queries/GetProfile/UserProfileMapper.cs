using SNHub.Users.Application.DTOs;
using SNHub.Users.Domain.Entities;

namespace SNHub.Users.Application.Queries.GetProfile;

public static class UserProfileMapper
{
    public static UserProfileDto ToDto(UserProfile p) => new(
        Id:                p.Id,
        UserId:            p.UserId,
        FirstName:         p.FirstName,
        LastName:          p.LastName,
        Email:             p.Email,
        PhoneNumber:       p.PhoneNumber,
        Headline:          p.Headline,
        Bio:               p.Bio,
        Location:          p.Location,
        ProfilePictureUrl: p.ProfilePictureUrl,
        LinkedInUrl:       p.LinkedInUrl,
        GitHubUrl:         p.GitHubUrl,
        WebsiteUrl:        p.WebsiteUrl,
        IsPublic:          p.IsPublic,
        YearsOfExperience: p.YearsOfExperience,
        Country:           p.Country,
        TimeZone:          p.TimeZone,
        IsDeleted:         p.IsDeleted,
        CreatedAt:         p.CreatedAt,
        UpdatedAt:         p.UpdatedAt);

    public static AdminUserDto ToAdminDto(UserProfile p) => new(
        Id:        p.Id,
        UserId:    p.UserId,
        FirstName: p.FirstName,
        LastName:  p.LastName,
        Email:     p.Email,
        PhoneNumber: p.PhoneNumber,
        Headline:  p.Headline,
        Location:  p.Location,
        Country:   p.Country,
        IsPublic:  p.IsPublic,
        IsDeleted: p.IsDeleted,
        DeletedAt: p.DeletedAt,
        CreatedAt: p.CreatedAt,
        UpdatedAt: p.UpdatedAt);
}
