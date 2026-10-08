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

    internal void Forget()
    {
        (View, Cursor, Anchor, Hover) = (null, null, null, null);
    }
}
