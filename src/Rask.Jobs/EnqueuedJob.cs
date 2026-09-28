namespace Rask.Background;

/// <summary>One job a test enqueued.</summary>
/// <typeparam name="TJob">The job's type.</typeparam>
/// <param name="Job">The job itself, to assert on its own properties.</param>
/// <param name="Delay">What <c>.In(…)</c> asked for, or <c>null</c>.</param>
/// <param name="Moment">What <c>.At(…)</c> asked for, or <c>null</c>.</param>
public sealed record EnqueuedJob<TJob>(TJob Job, TimeSpan? Delay, DateTimeOffset? Moment)
    where TJob : IJob;
