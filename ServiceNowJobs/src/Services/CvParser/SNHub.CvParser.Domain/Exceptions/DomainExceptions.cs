namespace SNHub.CvParser.Domain.Exceptions;

public sealed class DomainException(string msg) : Exception(msg);
public sealed class ParseResultNotFoundException(Guid id) : Exception($"Parse result {id} not found.");
public sealed class ParseResultAccessDeniedException() : Exception("You do not have access to this parse result.");
public sealed class InvalidFileTypeException(string allowed) : Exception($"Invalid file type. Allowed: {allowed}.");
public sealed class FileTooLargeException(int maxMb) : Exception($"File exceeds the maximum allowed size of {maxMb} MB.");
public sealed class ParseAlreadyAppliedException(Guid id) : Exception($"Parse result {id} has already been applied to your profile.");
public sealed class ParseNotCompletedException(Guid id) : Exception($"Parse result {id} is not yet completed. Current status: processing.");
