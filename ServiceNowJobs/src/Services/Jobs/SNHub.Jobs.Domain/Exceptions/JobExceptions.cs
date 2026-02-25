namespace SNHub.Jobs.Domain.Exceptions;

public sealed class JobNotFoundException : Exception
{
    public JobNotFoundException(Guid jobId)
        : base($"Job {jobId} was not found.") { }
}

public sealed class JobAccessDeniedException : Exception
{
    public JobAccessDeniedException()
        : base("You do not have permission to modify this job.") { }
}

public sealed class JobNotActiveException : Exception
{
    public JobNotActiveException(string message) : base(message) { }
}

public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
