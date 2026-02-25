namespace SNHub.Profiles.Domain.Exceptions;

public sealed class DomainException(string msg) : Exception(msg);
public sealed class ProfileNotFoundException(Guid userId) : Exception($"Profile for user {userId} not found.");
public sealed class ProfileAccessDeniedException() : Exception("You do not have permission to access this profile.");
public sealed class InvalidFileTypeException(string allowed) : Exception($"Invalid file type. Allowed: {allowed}.");
public sealed class FileTooLargeException(int maxMb) : Exception($"File exceeds the maximum size of {maxMb}MB.");
