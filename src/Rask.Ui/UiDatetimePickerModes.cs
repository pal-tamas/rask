namespace Rask;

/// <summary>
/// Flux UI's <c>mode</c> on <c>Ui.DatetimePicker</c>, as the step that opens it: <c>Ui.DatetimePicker.Range.Bind(…)</c>.
/// </summary>
/// <remarks>
/// The step hands back the entry of the picker that binds that mode's type, so a range bound to one moment does not
/// compile. <c>Ui.DatetimePicker</c> with no step is Flux's default: one moment.
/// </remarks>
public static class UiDatetimePickerModes
{
#pragma warning disable CA1822, S2325 // reached through the entry VALUE (Ui.DatetimePicker.Range): a static member would hang off the seed's type name instead
#pragma warning disable CA1720 // "Single" is Flux's own name for the mode
    extension(RaskSeed_UiDatetimePicker picker)
    {
        /// <summary>One moment — Flux's default, and what <c>Ui.DatetimePicker</c> is without a mode step.</summary>
        public RaskSeed_UiDatetimePicker Single => picker;

        /// <summary>A start and an end: binds a <see cref="UiDateTimeRange" />.</summary>
        public RaskSeed_UiDatetimePickerRange Range => default;

        /// <summary>The mode as a value, for when it is decided at run time; what is bound has to agree with it.</summary>
        /// <param name="mode">Which of Flux's modes.</param>
        public UiDatetimePickerModeEntry Mode(Ui.DatetimePickerMode mode) => new(mode);
    }

#pragma warning restore CA1720
#pragma warning restore CA1822, S2325

    internal static void Check(Ui.DatetimePickerMode? asked, Ui.DatetimePickerMode bound)
    {
        if (asked is { } mode && mode != bound)
        {
            throw new InvalidOperationException(
                $"Ui.DatetimePicker is in {mode} mode but what it binds is {bound}'s ({What(bound)}). "
                + $"Open it with Ui.DatetimePicker.{mode} and bind {What(mode)}.");
        }
    }

    private static string What(Ui.DatetimePickerMode mode) => mode == Ui.DatetimePickerMode.Range ? "a UiDateTimeRange" : "a DateTime";
}
