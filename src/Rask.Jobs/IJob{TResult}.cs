using Rask.Cqrs;

namespace Rask.Background;

/// <summary>
///     A job whose handler answers a <typeparamref name="TResult"/>:
///     <c>var done = await Jobs.Run(new AtPump(pump.Id)).InLane(pump.Id);</c>. Its handler is an
///     <see cref="IJobHandler{TJob, TResult}"/>. Run (<c>Jobs.Run</c>), the caller awaits the answer; enqueued
///     (<c>Jobs.Enqueue</c>), it runs like any job and the answer is kept on its row.
/// </summary>
/// <typeparam name="TResult">What the job's handler answers.</typeparam>
public interface IJob<out TResult> : IJob, ICommand<TResult>
{
    async Task<string?> IJob.SendThrough(IDispatcher dispatcher, CancellationToken cancellationToken) =>
        JobSerializerRegistry.SerializeResult(await dispatcher.Send<TResult>(this, cancellationToken).ConfigureAwait(false));
}
