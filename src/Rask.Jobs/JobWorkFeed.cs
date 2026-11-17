using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rask.Cqrs;

namespace Rask.Background;

/// <summary>
///     Publishes what watched jobs say (<see cref="WorkReported" />), one at a time and in the order they said it, off the jobs'
///     own flows: a job's <c>Jobs.Report(…)</c> hands its report over and goes on, and never waits for a handler of the event —
///     nor runs one in its own scope, beside its own <c>DbContext</c>. What it publishes reaches the open bells of this process;
///     the row is the truth for every other.
/// </summary>
internal sealed partial class JobWorkFeed(IServiceScopeFactory scopeFactory, ILogger logger)
{
    // Unbounded, and bounded all the same: a run says at most four reports a second, and its start and its end.
    private readonly Channel<WorkReported> _said = Channel.CreateUnbounded<WorkReported>(
        new UnboundedChannelOptions { SingleReader = true });

    /// <summary>Hands <paramref name="work" /> over to be published, and returns at once.</summary>
    public void Say(WorkReported work) => _said.Writer.TryWrite(work);

    /// <summary>
    ///     Publishes what is said until <paramref name="stop" />, then what was said before it — the ends of the jobs a
    ///     stopping host waited for included.
    /// </summary>
    public async Task Run(CancellationToken stop)
    {
        try
        {
            await foreach (var work in _said.Reader.ReadAllAsync(stop).ConfigureAwait(false))
            {
                await Publish(work, stop).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Stopping: what is left is published below, with no token to cut it short.
        }

        while (_said.Reader.TryRead(out var left))
        {
            await Publish(left, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task Publish(WorkReported work, CancellationToken cancellationToken)
    {
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using var publishing = scope.ConfigureAwait(false);
            await scope.ServiceProvider.GetRequiredService<IDispatcher>().Publish(work, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A handler of WorkReported that throws must not stop the feed: the next report still reaches the screen.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            PublishFailed(logger, ex, work.Id, work.State);
        }
    }

    [LoggerMessage(
        EventName = "JobWorkPublishFailed",
        Level = LogLevel.Warning,
        Message = "Publishing how job {Id} is getting on ({State}) failed; its row still says it, and the next report is published.")]
    private static partial void PublishFailed(ILogger logger, Exception exception, long id, WorkState state);
}
