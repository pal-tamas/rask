namespace Rask.Background;

/// <summary>
///     The job a caller awaited (<c>await Jobs.Run(job)</c>) was stopped before it finished — asked to, or at a limit.
///     <see cref="Stop" /> says why. A cancellation, so a catch of <see cref="OperationCanceledException" /> that already says
///     "it did not finish" keeps working; never a <see cref="JobFailedException" />, because a stopped job did not fail.
/// </summary>
public sealed class JobStoppedException : OperationCanceledException
{
    /// <summary>A job was stopped on request.</summary>
    public JobStoppedException()
        : this(JobStop.Asked)
    {
    }

    /// <summary>A job was stopped, with <paramref name="message" />.</summary>
    /// <param name="message">What to say.</param>
    public JobStoppedException(string message)
        : base(message)
    {
    }

    /// <summary>A job was stopped, with <paramref name="message" />, because of <paramref name="innerException" />.</summary>
    /// <param name="message">What to say.</param>
    /// <param name="innerException">What caused it.</param>
    public JobStoppedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>A job was stopped by <paramref name="token" />.</summary>
    /// <param name="token">The token that stopped it.</param>
    public JobStoppedException(CancellationToken token)
        : base(Describe(JobStop.Asked), token)
    {
    }

    /// <summary>A job was stopped by <paramref name="token" />, with <paramref name="message" />.</summary>
    /// <param name="message">What to say.</param>
    /// <param name="token">The token that stopped it.</param>
    public JobStoppedException(string? message, CancellationToken token)
        : base(message, token)
    {
    }

    /// <summary>A job was stopped by <paramref name="token" />, with <paramref name="message" />, because of <paramref name="innerException" />.</summary>
    /// <param name="message">What to say.</param>
    /// <param name="innerException">What caused it.</param>
    /// <param name="token">The token that stopped it.</param>
    public JobStoppedException(string? message, Exception? innerException, CancellationToken token)
        : base(message, innerException, token)
    {
    }

    /// <summary>A job was stopped because of <paramref name="stop" />.</summary>
    /// <param name="stop">Why.</param>
    public JobStoppedException(JobStop stop)
        : base(Describe(stop)) => Stop = stop;

    /// <summary>Why the job was stopped.</summary>
    public JobStop Stop { get; init; } = JobStop.Asked;

    /// <summary>The registered type name of the job that was stopped (<see cref="Job.Type" />), or null when none was given.</summary>
    public string? JobType { get; init; }

    internal static string Describe(JobStop stop) => stop switch
    {
        JobStop.TimeLimit => "The job was stopped at its time limit before it finished.",
        JobStop.Idle => "The job was stopped because it reported no progress for too long.",
        JobStop.DoneElsewhere => "The job was marked done, or deleted, outside the processor before it finished.",
        _ => "The job was stopped on request before it finished.",
    };
}
