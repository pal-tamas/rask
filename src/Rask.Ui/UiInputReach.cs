namespace Rask;

/// <summary>
///     Reaches the browser's own text input from a handler: what a render cannot say to an input that is being
///     typed into.
/// </summary>
/// <remarks>
///     The runtime leaves the text of a focused input to its reader, so a render cannot change what it says.
///     A list that writes its pick into the input — <c>Ui.Select</c>'s combobox, <c>Ui.Autocomplete</c>,
///     <c>Ui.Pillbox</c> — puts the text there through the element itself.
/// </remarks>
internal static class UiInputReach
{
    /// <summary>Replaces everything <paramref name="input" /> says with <paramref name="text" />.</summary>
    /// <param name="input">The input.</param>
    /// <param name="text">What it says from now on.</param>
    internal static async Task SayAsync(ElementRef<HTMLInputElement> input, string text)
    {
        // The whole text, however long it is: a range past the end stops at the end.
        await ReachAsync(() => input.SetRangeText(text, 0, int.MaxValue)).ConfigureAwait(false);
    }

    /// <summary>The same, leaving the caret after the new text: where typing goes on from.</summary>
    /// <param name="input">The input.</param>
    /// <param name="text">What it says from now on.</param>
    internal static async Task SayThenTypeOnAsync(ElementRef<HTMLInputElement> input, string text)
    {
        await ReachAsync(() => input.SetRangeText(text, 0, int.MaxValue, SelectionMode.End)).ConfigureAwait(false);
    }

    /// <summary>Runs <paramref name="call" /> against the browser, and does nothing where there is none.</summary>
    /// <param name="call">A call on an element.</param>
    internal static async Task ReachAsync(Func<ValueTask> call)
    {
        try
        {
            await call().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // No browser behind this render — a prerender, a test's page — so no element to reach. What the
            // render wrote is then all there is, and it says the same.
        }
    }
}
