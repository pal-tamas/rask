namespace Rask;

/// <summary>Puts the browser's focus on the day the keyboard moved to, once that day's button is drawn.</summary>
internal static class UiCalendarFocus
{
    internal static async Task MoveAsync(UiCalendarState state)
    {
        if (!state.FocusPending)
        {
            return;
        }

        state.FocusPending = false;
        try
        {
            await state.CursorRef.Focus().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Rendered with no browser behind it (a static render, a unit test): there is nothing to focus.
        }
    }
}
