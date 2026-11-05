using Rask.Cqrs;

namespace Rask.Background;

/// <summary>
/// A unit of background work. Enqueue it with <c>Jobs.Enqueue(job)</c>; the
/// <see cref="JobProcessor{TContext}"/> runs it later by handing it to its single
/// <see cref="IJobHandler{TJob}"/> — off the request thread, persisted durably, and retried on failure.
/// Underneath it travels through <c>Rask.Cqrs</c> as an <see cref="ICommand"/>, which is why a handler written
/// as <c>ICommandHandler&lt;TJob&gt;</c> also runs it.
/// </summary>
/// <remarks>A job whose handler answers a value is an <see cref="IJob{TResult}"/>, and <c>await Jobs.Run(job)</c> hands it back.</remarks>
[LocalOnly]
public interface IJob : ICommand
{
    /// <summary>
    ///     Sends this job to its handler and answers what the handler returned, as the JSON the row keeps — nothing for a
    ///     job that answers nothing. <see cref="IJob{TResult}"/> answers its result: the processor reads it without knowing
    ///     the type, and no reflection names it.
    /// </summary>
    internal async Task<string?> SendThrough(IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.Send(this, cancellationToken).ConfigureAwait(false);
        return null;
    }
}
