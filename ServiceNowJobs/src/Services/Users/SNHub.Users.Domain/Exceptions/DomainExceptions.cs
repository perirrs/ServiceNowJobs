namespace SNHub.Users.Domain.Exceptions;

public sealed class UserProfileNotFoundException(Guid userId)
    : Exception($"Profile for user {userId} not found.");

public sealed class UserAccessDeniedException()
    : Exception("You do not have permission to perform this action.");

public sealed class UserAlreadyDeletedException(Guid userId)
    : Exception($"User {userId} is already deleted.");

public sealed class InvalidFileTypeException(string allowed)
    : Exception($"Invalid file type. Allowed: {allowed}.");

public sealed class FileTooLargeException(int maxMb)
    : Exception($"File exceeds the maximum allowed size of {maxMb}MB.");
