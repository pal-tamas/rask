namespace Rask;

/// <summary>
/// Flux UI's <c>mode</c> on <c>Ui.DatePicker</c>, as the step that opens it: <c>Ui.DatePicker.Range.Bind(…)</c>.
/// </summary>
/// <remarks>
/// The step hands back the entry of the picker that binds that mode's type, so a range bound to a single day
/// does not compile. <c>Ui.DatePicker</c> with no step is Flux's default: one day.
/// </remarks>
public static class UiDatePickerModes
{
#pragma warning disable CA1822, S2325 // reached through the entry VALUE (Ui.Calendar.Range): a static member would hang off the seed's type name instead
#pragma warning disable CA1720 // "Single" is Flux's own name for the mode
    extension(RaskSeed_UiDatePicker picker)
    {
        /// <summary>One day — Flux's default, and what <c>Ui.DatePicker</c> is without a mode step.</summary>
        public RaskSeed_UiDatePicker Single => picker;

        /// <summary>A range of days: binds a <see cref="UiDateRange" />.</summary>
        public RaskSeed_UiDatePickerRange Range => default;

        /// <summary>The mode as a value, for when it is decided at run time; what is bound has to agree with it.</summary>
        /// <param name="mode">Which of Flux's modes.</param>
        public UiDatePickerModeEntry Mode(Ui.DatePickerMode mode) => new(mode);
    }

#pragma warning restore CA1720
#pragma warning restore CA1822, S2325

    internal static void Check(Ui.DatePickerMode? asked, Ui.DatePickerMode bound)
    {
        if (asked is { } mode && mode != bound)
        {
            throw new InvalidOperationException(
                $"Ui.DatePicker is in {mode} mode but what it binds is {bound}'s "
                + $"({What(bound)}). Open it with Ui.DatePicker.{mode} and bind {What(mode)}.");
        }
    }

    private static string What(Ui.DatePickerMode mode) => mode == Ui.DatePickerMode.Range ? "a UiDateRange" : "a DateOnly";
}
