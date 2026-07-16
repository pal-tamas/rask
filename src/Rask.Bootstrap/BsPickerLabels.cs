namespace Rask.Bootstrap;

// Localizable accessible names for the date/time picker chrome that has no CultureInfo source: the
// month-nav buttons, the time-column headings and the clear (×) button. Month/weekday names, the month
// label and per-date aria-labels already come from CultureInfo, so they are not repeated here. Pass a
// filled-in instance to a picker's Labels param to translate these; unset properties keep the English
// default. A record so callers can copy-and-tweak: `BsPickerLabels.Default with { Clear = "Törlés" }`.
public sealed record BsPickerLabels
{
    // The shared default (English). Pickers fall back to this when no Labels instance is supplied.
    public static readonly BsPickerLabels Default = new();

    // Accessible name for the previous-month nav button.
    public string PreviousMonth { get; init; } = "Previous month";

    // Accessible name for the next-month nav button.
    public string NextMonth { get; init; } = "Next month";

    // Accessible name for the hours listbox column.
    public string Hour { get; init; } = "Hour";

    // Accessible name for the minutes listbox column.
    public string Minute { get; init; } = "Minute";

    // Accessible name for the seconds listbox column (shown only when the picker enables seconds).
    public string Second { get; init; } = "Second";

    // Accessible name for the clear (×) button shown on a nullable picker that has a value.
    public string Clear { get; init; } = "Clear";
}
