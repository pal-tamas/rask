namespace Rask.TestSupport;

/// <summary>
/// The one polling wait shared by every suite. Lives here rather than in any single test project so a
/// test that needs to wait for a thread-pool continuation reaches for this instead of a fixed
/// <c>Task.Delay</c> budget — a duration is not a synchronisation primitive, and on a loaded machine the
/// gate failed on diffs that could not have caused it (#769).
/// </summary>
public static class WaitFor
{
    /// <summary>
    /// How long a wait for something that is on its way may run before the test calls it a hang.
    /// </summary>
    /// <remarks>
    /// Not a budget for the work. A wait ends the moment its condition holds, so this costs a passing run
    /// nothing; it is paid in full only by a test that was going to fail. A ceiling near the usual time is
    /// the other thing: a runner with every core taken spends seconds on what an idle machine does in a
    /// millisecond, and a two-second ceiling then fails tests that are right.
    /// </remarks>
    public static readonly TimeSpan HangCeiling = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Polls <paramref name="condition"/> until it holds, and <b>throws</b> if it has not by
    /// <see cref="HangCeiling"/>. The form to reach for: name the condition, not a number of seconds.
    /// </summary>
    /// <param name="condition">Polled every 15 ms until true.</param>
    /// <param name="reason">What the caller was waiting for, quoted in the exception.</param>
    public static Task True(Func<bool> condition, string? reason = null) => True(condition, HangCeiling, reason);

    /// <summary>
    /// Polls <paramref name="condition"/> until it holds, and <b>throws</b> if it never does within
    /// <paramref name="timeout"/>.
    /// </summary>
    /// <remarks>
    /// Throwing is the whole contract. Every caller asserts on state the condition is the precondition for,
    /// so a wait that returned quietly on timeout would hand the test a half-settled world and let it fail
    /// later on a confusing assertion — "expected 'the body text', got a spinner" — instead of here, where
    /// the actual problem is. <paramref name="reason"/> only enriches the message; its absence never turns
    /// the timeout back into a silent success.
    /// </remarks>
    /// <param name="condition">Polled every 15 ms until true.</param>
    /// <param name="timeout">
    /// How long to keep polling before giving up. Pass one only when the time IS the subject of the test.
    /// </param>
    /// <param name="reason">What the caller was waiting for, quoted in the exception.</param>
    public static async Task True(Func<bool> condition, TimeSpan timeout, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(15);
        }

        // One last look: the loop can exit with the deadline just passed while the condition became true
        // during the final delay, and failing then would be a flake of this helper's own making.
        if (condition())
        {
            return;
        }

        throw new TimeoutException(reason is null
            ? $"WaitFor timed out after {timeout}."
            : $"WaitFor timed out after {timeout}: {reason}");
    }
}
