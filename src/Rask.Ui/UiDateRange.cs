using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Rask;

/// <summary>
/// A stretch of days, first to last, both included — what a range calendar and a range date picker bind.
/// Flux UI's <c>DateRange</c>.
/// </summary>
/// <remarks>
/// <para>
/// Always whole. The reader's first click is held by the control and drawn as the start, and the model only
/// changes once the second click gives the range an end — so a bound model never holds half a range.
/// </para>
/// <para>
/// "Nothing chosen" is <c>default(UiDateRange)</c>, as it is <c>default(DateOnly)</c> for a single day — bind a
/// <c>UiDateRange?</c> where unset and a real range must differ.
/// </para>
/// <para>
/// One value in an address: <c>2026-10-01..2026-10-09</c>, and <c>~last7Days</c> after it when a preset made it. So
/// <c>[QueryParam("period")] public UiDateRange? Period { get; set; }</c> binds it as it binds a number, and text that
/// is no range leaves the property unset.
/// </para>
/// </remarks>
/// <param name="Start">The first day in the range.</param>
/// <param name="End">The last day in the range. The same as <paramref name="Start" /> for a range of one day.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct UiDateRange(DateOnly Start, DateOnly End) : IParsable<UiDateRange>
{
    private const string Iso = "yyyy-MM-dd";

    private const string Through = "..";

    private const char Named = '~';

    /// <summary>The preset this range was made from, when a date picker's preset list made it.</summary>
    public Ui.DateRangePreset? Preset { get; init; }

    /// <summary>How many days the range holds, its ends included; none for an unset range.</summary>
    public int Count => this == default ? 0 : End.DayNumber - Start.DayNumber + 1;

    /// <summary>Whether <paramref name="date" /> falls inside the range, its ends included.</summary>
    public bool Contains(DateOnly date) => this != default && date >= Start && date <= End;

    /// <summary>The range between two days, in whichever order they were picked.</summary>
    public static UiDateRange Between(DateOnly a, DateOnly b) => a <= b ? new(a, b) : new(b, a);

    /// <summary>The range a preset names, counted from <paramref name="today" />.</summary>
    /// <param name="preset">Which of Flux's presets.</param>
    /// <param name="today">The day the preset is counted from.</param>
    /// <param name="startDay">The first day of the week, for the week presets.</param>
    /// <param name="min">Where <see cref="Ui.DateRangePreset.AllTime" /> starts.</param>
    public static UiDateRange Of(Ui.DateRangePreset preset, DateOnly today, DayOfWeek startDay = DayOfWeek.Sunday, DateOnly? min = null) =>
        UiDateRangePresets.Resolve(preset, today, startDay, min) with { Preset = preset };

    /// <summary>The range as an address carries it: <c>2026-10-01..2026-10-09</c>; nothing for an unset range.</summary>
    /// <returns>The two days, and the preset's key after a <c>~</c> when a preset made the range.</returns>
    public override string ToString()
    {
        if (this == default)
        {
            return string.Empty;
        }

        var days = Start.ToString(Iso, CultureInfo.InvariantCulture) + Through + End.ToString(Iso, CultureInfo.InvariantCulture);
        return Preset is { } preset && preset != Ui.DateRangePreset.Custom ? days + Named + UiDateRangePresets.Key(preset) : days;
    }

    /// <summary>Reads a range written as <see cref="ToString" /> writes one.</summary>
    /// <param name="s">The text: <c>2026-10-01..2026-10-09</c>.</param>
    /// <param name="provider">Unused: the text is the same in every culture.</param>
    /// <exception cref="FormatException">The text is no range.</exception>
    public static UiDateRange Parse(string s, IFormatProvider? provider) =>
        TryParse(s, provider, out var range) ? range : throw new FormatException($"\"{s}\" is no range of days: write 2026-10-01..2026-10-09.");

    /// <summary>Reads a range written as <see cref="ToString" /> writes one; false for anything else.</summary>
    /// <param name="s">The text: <c>2026-10-01..2026-10-09</c>.</param>
    /// <param name="provider">Unused: the text is the same in every culture.</param>
    /// <param name="result">The range read, or an unset one.</param>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out UiDateRange result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        var text = s.Trim();
        Ui.DateRangePreset? preset = null;
        if (text.IndexOf(Named, StringComparison.Ordinal) is var at and >= 0)
        {
            preset = UiDateRangePresets.FromKey(text[(at + 1)..]);
            if (preset is null)
            {
                return false;
            }

            text = text[..at];
        }

        var parts = text.Split(Through);
        if (parts.Length != 2
            || !DateOnly.TryParseExact(parts[0], Iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || !DateOnly.TryParseExact(parts[1], Iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
        {
            return false;
        }

        result = Between(start, end) with { Preset = preset };
        return true;
    }
}
