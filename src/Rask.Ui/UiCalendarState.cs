namespace Rask;

/// <summary>What a calendar remembers between renders: where it is looking and where the keyboard is.</summary>
internal sealed class UiCalendarState
{
    /// <summary>The first shown month, once the reader has paged; unset while the selection decides.</summary>
    internal DateOnly? View { get; set; }

    /// <summary>The day the keyboard is on.</summary>
    internal DateOnly? Cursor { get; set; }

    /// <summary>A range's first day, picked and waiting for its last.</summary>
    internal DateOnly? Anchor { get; set; }

    /// <summary>The day a waiting range would end on: the one under the pointer or the keyboard.</summary>
    internal DateOnly? Hover { get; set; }

    /// <summary>Set when a paging key moved the view: the next render names no day for the focus to follow.</summary>
    internal bool Dropped { get; set; }

    /// <summary>Set by the render that named none, for <see cref="RenameAsync" /> to answer.</summary>
    internal bool Unnamed { get; set; }

    /// <summary>
    ///     After the render a paging key caused: once the browser has let the focus fall, the tab stop is named
    ///     again, so an arrow pressed on it later carries the focus as it did before.
    /// </summary>
    internal async Task RenameAsync(Action render)
    {
        if (!Unnamed)
        {
            return;
        }

        Unnamed = false;
        // A task of its own, after the one that drew the unnamed grid: the runtime has to see that one first.
        await Task.Delay(1).ConfigureAwait(false);
        render();
    }

    internal void Forget()
    {
        (View, Cursor, Anchor, Hover) = (null, null, null, null);
    }
}
