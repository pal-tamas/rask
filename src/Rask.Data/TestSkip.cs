namespace Rask.Data;

/// <summary>
///     Ends the running test as SKIPPED rather than failed, without Rask.Data referencing a test framework: xUnit v3
///     reports any exception whose message starts with its dynamic-skip token (<c>Xunit.Sdk.DynamicSkipToken</c>) as a
///     skip, with the rest of the message as the reason — the same thing its own <c>Assert.Skip</c> throws.
/// </summary>
internal static class TestSkip
{
    /// <summary>xUnit v3's <c>DynamicSkipToken.Value</c>.</summary>
    public const string DynamicSkipToken = "$XunitDynamicSkip$";

    /// <summary>The exception that skips the test, giving <paramref name="reason" />.</summary>
    public static InvalidOperationException Because(string reason) => new(DynamicSkipToken + reason);
}
