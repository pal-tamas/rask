using Rask.Cqrs;

namespace Rask.Background;

/// <summary>
/// Runs one kind of <see cref="IJob"/>. Write one per job type; the build finds it, registers it, and the
/// <see cref="JobProcessor{TContext}"/> hands it every job of that type it takes off the backlog — retrying
/// it on failure, so <see cref="Handle"/> should be safe to run twice.
/// </summary>
/// <remarks>
/// It is wired exactly like a command handler, because underneath it is one: a job reaches its handler
/// through <c>Rask.Cqrs</c>' dispatcher, which is what gives it the same pipeline (validation, behaviors)
/// and the same one-handler-per-type rule (RASK028). An existing <c>ICommandHandler&lt;TJob&gt;</c> keeps
/// working; this is the name that says what the class is for.
/// </remarks>
/// <typeparam name="TJob">The job type.</typeparam>
public interface IJobHandler<in TJob> : ICommandHandler<TJob>
    where TJob : IJob
{
    /// <summary>Does the work. Cancelled when the host shuts down — read <c>Current.Cancellation</c> to pass on.</summary>
    Task Handle(TJob job);

    /// <summary>The dispatcher's entry point, forwarded to <see cref="Handle(TJob)"/>.</summary>
    Task ICommandHandler<TJob>.Handle(TJob command) => Handle(command);
}
