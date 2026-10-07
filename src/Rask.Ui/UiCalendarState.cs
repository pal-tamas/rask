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

    /// <summary>Set when a key moved the cursor: the day's button takes the focus once it is drawn.</summary>
    internal bool FocusPending { get; set; }

    internal ElementRef<HTMLButtonElement> CursorRef { get; } = new();

    internal void Forget()
    {
        (View, Cursor, Anchor, Hover) = (null, null, null, null);
    }
}
