namespace Rask;

/// <summary>Calls into the browser through an element's ref, where a render alone cannot say what is wanted.</summary>
internal static class UiReach
{
    /// <summary>Runs <paramref name="call" />, and lets it go when no browser is behind this render.</summary>
    internal static async Task Async(Func<ValueTask> call)
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

    /// <summary>
    ///     Starts <paramref name="call" /> and does not wait for it: for a call whose answer nobody needs
    ///     (focus, blur, a step), from an event handler.
    /// </summary>
    /// <remarks>
    ///     A call made in a handler leaves for the browser with the frame that handler's render sends. Awaited
    ///     there, the handler waits for a frame of its own first, and every event behind it waits too.
    /// </remarks>
    internal static void Send(Func<ValueTask> call) => _ = Async(call);

    /// <summary>Puts <paramref name="text" /> into an input the reader may be typing in.</summary>
    /// <remarks>
    ///     The runtime leaves the text of an input that is being typed into to its reader, so a render cannot
    ///     change what a focused input says. Its text is put there through the element itself.
    /// </remarks>
    internal static Task SayAsync(ElementRef<HTMLInputElement> input, string text) =>
        // The whole text, however long it is: a range past the end stops at the end.
        Async(() => input.SetRangeText(text, 0, int.MaxValue));
}
