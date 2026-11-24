using Rask.Wire;

namespace Rask.Testing;

public static partial class Test
{
    /// <summary>How long two operations run side by side may take before the test calls it a deadlock.</summary>
    private static readonly TimeSpan DeadlockAfter = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Starts <paramref name="first" /> and <paramref name="second" /> at the same moment and checks that exactly one
    ///     of them went through and the other was refused — not both, not neither, and no deadlock — handing back the
    ///     refusal: <c>(await Test.Concurrently(() =&gt; Supply.Change(id, version, a), () =&gt; Supply.Change(id, version, b))).Whole()</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each operation runs on a thread of its own, released together, so neither has finished before the other
    ///         starts; both run in the test's flow, so they reach the database the test started. A refusal is what
    ///         <see cref="Refuses(Func{Task})" /> takes for one: any exception that names the fields it is about — a stale
    ///         version, a unique index, a rule a lock made the second one see.
    ///     </para>
    ///     <para>
    ///         Both going through, both refused, any other exception, or the two still running after 30 seconds (two
    ///         operations waiting on each other) fails the test with a <see cref="RefusalAssertionException" /> that
    ///         says which.
    ///     </para>
    /// </remarks>
    /// <param name="first">One of the two operations.</param>
    /// <param name="second">The other.</param>
    /// <returns>The refusal of the one that lost.</returns>
    /// <exception cref="RefusalAssertionException">It was not exactly one through and one refused.</exception>
    public static async Task<RefusalAssertion> Concurrently(Func<Task> first, Func<Task> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var one = Task.Run(async () =>
        {
            await start.Task.ConfigureAwait(false);
            await first().ConfigureAwait(false);
        });
        var other = Task.Run(async () =>
        {
            await start.Task.ConfigureAwait(false);
            await second().ConfigureAwait(false);
        });
        start.SetResult();

        // Waited on without awaiting: each outcome, a failure included, is read off its own task below.
        var both = Task.WhenAll(one, other);
        using var deadline = new CancellationTokenSource();
        if (await Task.WhenAny(both, Task.Delay(DeadlockAfter, deadline.Token)).ConfigureAwait(false) != both)
        {
            var running = one.IsCompleted || other.IsCompleted ? "one is still running" : "neither has finished — a deadlock";
            throw new RefusalAssertionException(
                $"Expected one to go through and one to be refused; after {DeadlockAfter.TotalSeconds:0} seconds {running}.");
        }

        await deadline.CancelAsync().ConfigureAwait(false);
        return Outcome(Ended(one), Ended(other));
    }

    // Null for an operation that went through, else what it ended in.
    private static Exception? Ended(Task operation)
    {
        if (operation.IsFaulted)
        {
            return operation.Exception.InnerException;
        }

        return operation.IsCanceled ? new TaskCanceledException(operation) : null;
    }

    private static RefusalAssertion Outcome(Exception? first, Exception? second)
    {
        if (first is null && second is null)
        {
            throw new RefusalAssertionException("Expected one to go through and one to be refused; both went through.");
        }

        if (first is IFieldFailures && second is IFieldFailures)
        {
            throw new RefusalAssertionException(
                $"Expected one to go through and one to be refused; both were refused: \"{first.Message}\" and \"{second.Message}\".", first);
        }

        if (new[] { first, second }.FirstOrDefault(static e => e is not null and not IFieldFailures) is { } other)
        {
            throw new RefusalAssertionException(
                $"Expected one to go through and one to be refused; one threw {other.GetType().Name}: {other.Message}", other);
        }

        // Here exactly one is null and the other a refusal.
        return new RefusalAssertion(((IFieldFailures)(first ?? second)!).Failures);
    }
}
