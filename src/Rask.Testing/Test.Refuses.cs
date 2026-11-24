using Rask.Wire;

namespace Rask.Testing;

public static partial class Test
{
    /// <summary>
    ///     Runs <paramref name="act" /> and hands back the refusal it ended in, to say where it was refused:
    ///     <c>(await Test.Refuses(() =&gt; Dispatcher.Send(new Rename(id, "")))).At("Name")</c>.
    /// </summary>
    /// <remarks>
    ///     A refusal is any exception that names the fields it is about — a failed rule, <c>Refuse.When(…)</c>, a value
    ///     object's <c>Create</c>, a unique index the database enforced. Anything else, or nothing at all, fails the test
    ///     with a <see cref="RefusalAssertionException" /> that says what happened instead.
    /// </remarks>
    /// <param name="act">The operation expected to be refused.</param>
    /// <returns>What was refused, and where.</returns>
    /// <exception cref="RefusalAssertionException"><paramref name="act" /> was not refused.</exception>
    public static async Task<RefusalAssertion> Refuses(Func<Task> act)
    {
        ArgumentNullException.ThrowIfNull(act);
        try
        {
            await act().ConfigureAwait(false);
        }
        catch (Exception refused) when (refused is IFieldFailures failures)
        {
            return new RefusalAssertion(failures.Failures);
        }
#pragma warning disable CA1031 // Any other exception is the test's failure, said as one: it is kept as the inner exception.
        catch (Exception other)
#pragma warning restore CA1031
        {
            throw NotRefused(other);
        }

        throw NotRefused(null);
    }

    /// <summary>
    ///     Runs <paramref name="act" /> and hands back the refusal it ended in, to say where it was refused:
    ///     <c>Test.Refuses(() =&gt; KmRange.Create(10, 5, "Km")).At("Km.End")</c>.
    /// </summary>
    /// <param name="act">The operation expected to be refused.</param>
    /// <returns>What was refused, and where.</returns>
    /// <exception cref="RefusalAssertionException"><paramref name="act" /> was not refused.</exception>
    public static RefusalAssertion Refuses(Action act)
    {
        ArgumentNullException.ThrowIfNull(act);
        try
        {
            act();
        }
        catch (Exception refused) when (refused is IFieldFailures failures)
        {
            return new RefusalAssertion(failures.Failures);
        }
#pragma warning disable CA1031 // Any other exception is the test's failure, said as one: it is kept as the inner exception.
        catch (Exception other)
#pragma warning restore CA1031
        {
            throw NotRefused(other);
        }

        throw NotRefused(null);
    }

    private static RefusalAssertionException NotRefused(Exception? instead) =>
        instead is null
            ? new RefusalAssertionException("Expected a refusal; it went through.")
            : new RefusalAssertionException($"Expected a refusal; it threw {instead.GetType().Name}: {instead.Message}", instead);
}
