namespace Rask.Background;

/// <summary>
///     The job a caller awaited (<c>await Jobs.Run(job)</c>) failed: <see cref="Exception.Message"/> is the error its handler
///     threw, as the job's row recorded it — on whichever host ran it. The job is a dead letter; it is not retried, so the
///     caller decides whether to run it again.
/// </summary>
public sealed class JobFailedException : Exception
{
    /// <summary>A job failed, for no reason given.</summary>
    public JobFailedException()
    {
    }

    /// <summary>A job failed with <paramref name="message"/>.</summary>
    /// <param name="message">The error its handler threw.</param>
    public JobFailedException(string message)
        : base(message)
    {
    }

    /// <summary>A job failed with <paramref name="message"/>, because of <paramref name="innerException"/>.</summary>
    /// <param name="message">The error its handler threw.</param>
    /// <param name="innerException">What caused it.</param>
    public JobFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The registered type name of the job that failed (<see cref="Job.Type"/>), or null when none was given.</summary>
    public string? JobType { get; init; }
}
