using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     Says which of an element's handlers only draw their control: <c>data-rask-draws="toggle keydown"</c>.
/// </summary>
/// <remarks>
///     <para>
///     A bound control waits for the reader's next action, and what waits goes ahead of every event the page is
///     sent. A control the kit draws in C# is sent events that are no action — a list opened, a row the pointer
///     is over, the next month — only because the page is what draws it; Flux makes no request for them at all.
///     The runtime sends an event named here alone and marked (<c>rask-owned.ts</c>): nothing waiting is written
///     for it, in the browser or in the session.
///     </para>
///     <para>
///     <b>Name a handler only when it can reach nothing of the app's.</b> One that calls a callback the app
///     gave, or writes a value now — a pick of a control that says <c>.Live()</c>, or whose value the page
///     holds — is an action, and is left out. A pick of a bound control that waits is drawing: the value itself
///     is held for the next action (<see cref="Waits" />).
///     </para>
/// </remarks>
internal static class UiDraws
{
    /// <summary>The attribute.</summary>
    internal const string Mark = "data-rask-draws";

    /// <summary>Whether what <paramref name="control" /> commits is held for the next action: it is bound, and does not say <c>.Live()</c>.</summary>
    /// <param name="control">The control that commits.</param>
    /// <param name="bound">Whether it was opened with <c>Bind</c>.</param>
    internal static bool Waits(Component control, bool bound) => bound && PendingBinds.Waits(control);

    /// <summary>The mark for <c>Attributes(…)</c>: the events of <paramref name="heard" /> that only draw, or nothing when none does.</summary>
    /// <param name="heard">Each event the element hears, and whether its handler only draws.</param>
    internal static Dictionary<string, string?> Marks(params (string Event, bool Draws)[] heard) =>
        With(new Dictionary<string, string?>(StringComparer.Ordinal), heard);

    /// <summary>The same as the pair to spread among an element's other pairs: one, or none.</summary>
    /// <param name="heard">Each event the element hears, and whether its handler only draws.</param>
    internal static (string, string?)[] Pairs(params (string Event, bool Draws)[] heard) =>
        UiMarks.Present((Mark, Names(heard)));

    /// <summary><paramref name="marks" /> with the mark among them, in a dictionary of its own when there is one to add.</summary>
    /// <param name="marks">The element's other attributes.</param>
    /// <param name="heard">Each event the element hears, and whether its handler only draws.</param>
    internal static Dictionary<string, string?> With(Dictionary<string, string?> marks, params (string Event, bool Draws)[] heard) =>
        Names(heard) is { } names
            ? new Dictionary<string, string?>(marks, StringComparer.Ordinal) { [Mark] = names }
            : marks;

    private static string? Names((string Event, bool Draws)[] heard)
    {
        var names = string.Join(' ', heard.Where(static one => one.Draws).Select(static one => one.Event));
        return names.Length == 0 ? null : names;
    }
}
