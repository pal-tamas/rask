namespace Rask;

/// <summary>
///     A kit control that another draws as one of its parts — the day and the time of <see cref="UiDayAndTime{T}" /> —
///     and that hands what it cannot take to that one: a date half typed, a time outside its limits. Bound to
///     nothing of its own, it has no field to say it under; the control it is part of has.
/// </summary>
/// <remarks>
///     The part reads the <see cref="UiUnreadRelay" /> the other provides around it as it renders
///     (<see cref="UiUnreadRelay.Around" />), so the instance the page keeps holds it, whichever render drew it.
/// </remarks>
internal interface IUiRelaysUnread
{
    /// <summary>Hears what the part would have said under its field, in the reader's language. Unset, nothing is said.</summary>
    Func<string, Task>? Relay { get; }
}
