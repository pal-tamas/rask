using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>
///     The kit's day and time in one row — not a Flux component, Flux's typed date picker and time picker side by side, an
///     addition the owner approved — its examples with their code, and its reference.
/// </summary>
[Route("day-and-time")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiDayAndTimePage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.DayAndTime.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.DayAndTime)
            .Lead("A moment asked as a day and a time of that day, in one row: the day typed in the reader's own order or "
                  + "picked from the calendar, the time typed beside it. One DateTime? is bound, and the time goes beneath "
                  + "the day where the row has no room, as on a phone. Not one of Flux UI's components: Flux's own date "
                  + "picker and time picker, side by side.")
            .Api(UiApi.DayAndTime)[
            UiDocsExample.Key("ui-day-and-time").Heading("A day and a time").File("DayAndTimeExample.cs").Demo(DayAndTimeExample).Testid("ui-day-and-time")
                .Blurb("The time waits for a day: with no day there is no time to give, and a day taken away takes its time "
                       + "with it. A first day is its first minute. The member is a DateTime? nothing requires, so the day "
                       + "has its clear button; half typed, the pair says so under it and the save is refused."),
            UiDocsExample.Key("ui-day-and-time-seconds").Heading("Seconds kept").File("DayAndTimeSecondsExample.cs").Demo(DayAndTimeSecondsExample).Testid("ui-day-and-time-seconds")
                .Blurb("Typed in Hungarian, 2026. 11. 06., and required, so no clear button. The time field writes hours and "
                       + "minutes: a new day keeps the moment's time to the second, and a time left at the hour and minute "
                       + "it shows keeps the seconds too. Any other time has none.")
        ];
}
