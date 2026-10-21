using Microsoft.EntityFrameworkCore;

namespace Rask.Providers.E2E.Tests;

/// <summary>
///     Ten hosts, each inside its own <c>Db.Transaction</c>, enqueue the same work <c>UnlessWaiting</c> and one job of
///     their own. A collision must answer <c>false</c> and leave the rest of the transaction to commit — on SQL Server
///     under <c>XACT_ABORT</c>, a refused insert would doom the whole transaction instead.
/// </summary>
internal static class UnlessWaitingInTransactions
{
    internal const int Hosts = 10;

    /// <summary>What each host's UnlessWaiting answered, or the exception its transaction ended in.</summary>
    internal static async Task<(bool Written, Exception? Failure)[]> Run(Func<IJobs> newQueue, Func<DbContext> open)
    {
        var queues = Enumerable.Range(0, Hosts).Select(_ => newQueue()).ToList();
        Db.Configure(open);
        try
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var hosts = queues.Select(async (queue, i) =>
            {
                await start.Task;
                try
                {
                    var written = await Db.Transaction(async () =>
                    {
                        var shared = await queue.Enqueue(new ProbeJob("ask"), TestContext.Current.CancellationToken)
                            .InLane("pump-7").UnlessWaiting();
                        await queue.Enqueue(new ProbeJob($"own-{i}"), TestContext.Current.CancellationToken);
                        return shared;
                    }, cancellationToken: TestContext.Current.CancellationToken);
                    return (written, (Exception?)null);
                }
#pragma warning disable CA1031 // the test asserts on what each host's transaction ended in, whatever it was
                catch (Exception e)
#pragma warning restore CA1031
                {
                    return (false, e);
                }
            }).ToList();
            start.SetResult();
            return await Task.WhenAll(hosts);
        }
        finally
        {
            Db.Reset();
        }
    }
}
