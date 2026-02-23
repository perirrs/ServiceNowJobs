namespace SNHub.Profiles.Domain.Exceptions;
public sealed class DomainException(string msg) : Exception(msg);
public sealed class ProfileNotFoundException(Guid userId) : Exception($"Profile for user {userId} not found.");
