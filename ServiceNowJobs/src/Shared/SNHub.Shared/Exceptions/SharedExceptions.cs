namespace SNHub.Shared.Exceptions;

public class AppException : Exception
{
    public AppException(string message) : base(message) { }
}

public sealed class NotFoundException : AppException
{
    public NotFoundException(string resource, object id)
        : base($"{resource} '{id}' was not found.") { }
}

public sealed class ForbiddenException : AppException
{
    public ForbiddenException(string message = "You do not have permission.")
        : base(message) { }
}
