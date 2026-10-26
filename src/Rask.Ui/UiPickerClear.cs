namespace Rask;

/// <summary>
///     The button that empties a date or a time picker, laid over the end of its trigger before the chevron —
///     Flux UI's <c>clearable</c>.
/// </summary>
/// <remarks>
///     A slot placed against the nearest positioned box and a button inside it: the button's own look is
///     <c>relative</c>, so the two cannot be one element. The slot hangs from the top and is as tall as the
///     trigger, not as its room, so a row that stretches the picker leaves the button where the trigger is.
/// </remarks>
internal abstract partial class UiPickerClear : global::Rask.Core.RaskMarkup
{
    private const string Slot = "absolute top-0 end-9 flex items-center";

    private const string SlotHidden = "absolute top-0 end-9 hidden items-center";

    /// <summary>
    ///     The button for a picker whose root is the positioned box: keyed, so its coming and going beside the
    ///     trigger and the list is an insert or a removal.
    /// </summary>
    /// <param name="height">The trigger's own height, as a class.</param>
    /// <param name="clear">Empties the picker.</param>
    internal static global::Rask.Core.Component Render(string height, Func<Task> clear) =>
        Div.Key("clear").Class(UiClass.Compose(Slot, height))[Press(clear)];

    /// <summary>
    ///     <paramref name="trigger" /> in a box of its own with the button over its end. The button is always
    ///     drawn and out of sight while <paramref name="shown" /> is false, so what follows the trigger keeps
    ///     the handlers it had when a date is chosen or taken away.
    /// </summary>
    /// <param name="trigger">The button or the typed field.</param>
    /// <param name="height">The trigger's own height, as a class.</param>
    /// <param name="clear">Empties the picker.</param>
    /// <param name="shown">Whether there is something to empty.</param>
    /// <param name="part">What a runtime that draws the picker calls the slot, or nothing.</param>
    /// <param name="label">The button's accessible name; "Clear" unless given.</param>
    internal static global::Rask.Core.Component Over(
        global::Rask.Core.Component trigger, string height, Func<Task> clear, bool shown, (string, string?)? part = null, string? label = null) =>
        Div.Class("relative")[
            trigger,
            Div.Class(UiClass.Compose(shown ? Slot : SlotHidden, height)).Attributes(part is { } p ? [p] : [])[Press(clear, label)]
        ];

    private static global::Rask.Core.Component Press(Func<Task> clear, string? label = null) =>
        Button
            .Type(ButtonType.Button)
            .Class(UiInputLook.Action)
            .Aria("label", label ?? RaskStrings.Get(RaskString.PickerClear, "Clear"))
            .Data("ui-button", null)
            .OnClick(clear)[
            Ui.Icon.Name(Ui.IconName.XMark).Mini
        ];
}
