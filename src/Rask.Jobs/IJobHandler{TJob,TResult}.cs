using Rask.Cqrs;

namespace Rask.Background;

/// <summary>
///     Runs one kind of <see cref="IJob{TResult}"/> and answers its result — what <c>await Jobs.Run(job)</c> hands back to
///     the caller. Write one per job type; the build finds it and registers it, as it does an <see cref="IJobHandler{TJob}"/>.
/// </summary>
/// <remarks>Underneath it is an <see cref="ICommandHandler{TCommand, TResult}"/>, so the dispatcher's pipeline runs it.</remarks>
/// <typeparam name="TJob">The job type.</typeparam>
/// <typeparam name="TResult">What the job answers.</typeparam>
public interface IJobHandler<in TJob, TResult> : ICommandHandler<TJob, TResult>
    where TJob : IJob<TResult>
{
    /// <summary>Does the work and answers it. Read <c>Current.Cancellation</c> to pass the host's shutdown on.</summary>
    Task<TResult> Handle(TJob job);

    /// <summary>The dispatcher's entry point, forwarded to <see cref="Handle(TJob)"/>.</summary>
    Task<TResult> ICommandHandler<TJob, TResult>.Handle(TJob command) => Handle(command);
}
