namespace Rask.Core.Forms;

// A control that draws itself from several elements and commits its value from a handler of its own — a
// listbox, a calendar, a group of boxes. Bound, it hands the value to the session (PendingBinds), which writes
// it with the next action, as a bound input's value travels with one; `.Live()` writes it as it is chosen.
//
// A part of such a control that registers handlers of its own (a tab of the tabs, a radio of the menu's group)
// answers for the control it belongs to, so its events are not mistaken for an action either.
internal interface IDeferredBind
{
    /// <summary>Whether the value is written as it is chosen. Unset, it waits for the next action.</summary>
    bool? Live { get; }
}
