namespace Rask;

/// <summary>
///     The × that empties a picker or a listbox, inside its trigger between the choice and the chevron — Flux UI's
///     <c>clearable</c>, drawn as Flux draws it.
/// </summary>
/// <remarks>
///     Measured on Flux's datetime picker (2026-10-26): a 32px <c>div</c> in the trigger's row, taking its room so the
///     words never run under it, out of the Tab order (<c>tabindex="-1"</c>, as Flux's: Backspace and the list reach
///     the same end), its press the trigger's no more — the popup does not open. Once it has cleared, it goes, and
///     the focus with it.
/// </remarks>
internal abstract partial class UiPickerClear : global::Rask.Core.RaskMarkup
{
    private const string Shown =
        "relative flex items-center justify-center gap-2 size-8 ms-2 -me-2 rounded-md cursor-pointer text-sm font-medium whitespace-nowrap "
        + "text-zinc-500 hover:text-zinc-800 hover:bg-zinc-800/5 dark:text-zinc-400 dark:hover:text-white dark:hover:bg-white/15";

    private const string Hidden =
        "relative hidden items-center justify-center gap-2 size-8 ms-2 -me-2 rounded-md cursor-pointer text-sm font-medium whitespace-nowrap "
        + "text-zinc-500 hover:text-zinc-800 hover:bg-zinc-800/5 dark:text-zinc-400 dark:hover:text-white dark:hover:bg-white/15";

    /// <summary>The × to write in the trigger before its chevron, keyed so its coming and going is an insert or a removal.</summary>
    /// <param name="clear">Empties the picker.</param>
    /// <param name="shown">Whether there is something to empty; a runtime that draws the picker may show it later.</param>
    /// <param name="label">Its accessible name; "Clear" unless given.</param>
    /// <param name="marks">What a runtime that draws the picker calls it, or the frames its press may send.</param>
    internal static global::Rask.Core.Component Inside(
        Func<Task> clear, bool shown = true, string? label = null, params (string Name, string? Value)[] marks)
    {
        var x = Div
            .Key("clear")
            .TabIndex(-1)
            .Class(shown ? Shown : Hidden)
            .Aria("label", label ?? RaskStrings.Get(RaskString.PickerClear, "Clear"))
            .Data("ui-button", "")
            .OnClick(clear);
        return (marks.Length > 0 ? x.Attributes(marks) : x)[Ui.Icon.Name(Ui.IconName.XMark).Micro];
    }
}
