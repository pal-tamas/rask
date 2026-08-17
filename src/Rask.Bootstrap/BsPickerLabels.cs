namespace Rask.Bootstrap;

// Localizable accessible names for the date/time picker chrome that has no CultureInfo source: the
// month-nav buttons, the time-column headings and the clear (×) button. Month/weekday names, the month
// label and per-date aria-labels already come from CultureInfo, so they are not repeated here. Pass a
// filled-in instance to a picker's Labels param to translate these; unset properties keep the English
// default. A record so callers can copy-and-tweak: `BsPickerLabels.Default with { Clear = "Törlés" }`.

/// <summary>
///     The user-visible strings inside a date or time picker, so they can be translated rather than
///     hard-coded in English.
/// </summary>
public sealed record BsPickerLabels
{
    // The shared default (English). Pickers fall back to this when no Labels instance is supplied.
    public static readonly BsPickerLabels Default = new();

    // Accessible name for the previous-month nav button.

    /// <summary>The previous-month control's accessible name.</summary>
    public string PreviousMonth { get; init; } = "Previous month";

    // Accessible name for the next-month nav button.

    /// <summary>The next-month control's accessible name.</summary>
    public string NextMonth { get; init; } = "Next month";

    // Accessible name for the hours listbox column.

    /// <summary>The hour column's label.</summary>
    public string Hour { get; init; } = "Hour";

    // Accessible name for the minutes listbox column.

    /// <summary>The minute column's label.</summary>
    public string Minute { get; init; } = "Minute";

    // Accessible name for the seconds listbox column (shown only when the picker enables seconds).

    /// <summary>The seconds column's label.</summary>
    public string Second { get; init; } = "Second";

    // Accessible name for the clear (×) button shown on a nullable picker that has a value.

    /// <summary>The clear control's label.</summary>
    public string Clear { get; init; } = "Clear";
}
