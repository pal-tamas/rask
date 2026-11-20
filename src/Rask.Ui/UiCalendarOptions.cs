using System.Globalization;
using Microsoft.Extensions.DependencyInjection;

namespace Rask;

/// <summary>Flux's calendar props, resolved: what a calendar or a date picker hands the grid.</summary>
internal sealed record UiCalendarOptions(
    DateOnly Today,
    CultureInfo Culture,
    DayOfWeek StartDay,
    Ui.CalendarSize Size,
    int Months)
{
    internal DateOnly? Min { get; init; }

    internal DateOnly? Max { get; init; }

    internal IReadOnlyCollection<DateOnly>? Unavailable { get; init; }

    internal int? MinRange { get; init; }

    internal int? MaxRange { get; init; }

    internal DateOnly? OpenTo { get; init; }

    internal bool ForceOpenTo { get; init; }

    internal bool Navigation { get; init; } = true;

    internal bool Static { get; init; }

    internal bool WeekNumbers { get; init; }

    internal bool SelectableHeader { get; init; }

    internal bool WithToday { get; init; }

    internal bool FixedWeeks { get; init; }

    /// <summary>Inputs over the months to type the choice into (<c>with-inputs</c>); none when null.</summary>
    internal Ui.DatePickerInputVariant? WithInputs { get; init; }


    internal string? Class { get; init; }

    internal string? Id { get; init; }

    internal IReadOnlyDictionary<string, string?>? Data { get; init; }

    /// <summary>
    ///     The grid sits in a popup: the day Tab would land on asks for the focus when the popup opens, so it
    ///     does not fall on the first control in the header.
    /// </summary>
    internal bool InPopup { get; init; }


    /// <summary>Whether the reader can press <paramref name="day" />: within the limits, and not unavailable.</summary>
    internal bool Offers(DateOnly day) =>
        (Min is not { } min || day >= min) && (Max is not { } max || day <= max) && Unavailable?.Contains(day) != true;

    /// <summary>
    ///     Today on the reader's calendar (<see cref="Reader.Today" />): the zone their browser named, on the app's
    ///     clock — not the server's. Outside a page (a markup render with no services) the app's zone.
    /// </summary>
    internal static DateOnly Now() => ReaderOfPage().Today;

    /// <summary>Now on the reader's wall (<see cref="Reader.Now" />), to the minute a time picker starts from.</summary>
    internal static TimeOnly TimeNow() => TimeOnly.FromDateTime(ReaderOfPage().Now.DateTime);

    private static Reader ReaderOfPage() => Ambient.Services?.GetService<Reader>() ?? new Reader();
}
