using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's calendar as a C# component: each example with its own code, and its reference.</summary>
[Route("calendar")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiCalendarPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Calendar.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Calendar)
            .Lead("A month of days to pick from: one day, several, or a range, for scheduling and booking. The step that "
                  + "opens it says which, and the type it binds follows.")
            .Api(UiApi.Calendar)[
            UiDocsExample.Key("ui-calendar").Heading("Calendar").File("CalendarExample.cs").Demo(CalendarExample).Testid("ui-calendar"),
            UiDocsExample.Key("ui-calendar-bound").Heading("Basic usage").File("CalendarBoundExample.cs").Demo(CalendarBoundExample).Testid("ui-calendar-bound")
                .Blurb("Bound to a DateOnly that starts on today, and written as a day is picked."),
            UiDocsExample.Key("ui-calendar-multiple").Heading("Multiple dates").File("CalendarMultipleExample.cs").Demo(CalendarMultipleExample).Testid("ui-calendar-multiple")
                .Blurb("Several days that need not be next to each other, held in a collection."),
            UiDocsExample.Key("ui-calendar-range").Heading("Date range").File("CalendarRangeExample.cs").Demo(CalendarRangeExample).Testid("ui-calendar-range")
                .Blurb("Two months and a UiDateRange. The first click is drawn as the start and written nowhere; the second writes the whole range."),
            UiDocsExample.Key("ui-calendar-range-limits").Heading("Range configuration").File("CalendarRangeLimitsExample.cs").Demo(CalendarRangeLimitsExample).Testid("ui-calendar-range-limits")
                .Blurb("A range of three to ten days: while it waits for its end, the days that would make it too short or too long are disabled."),
            UiDocsExample.Key("ui-calendar-size").Heading("Size").File("CalendarSizeExample.cs").Demo(CalendarSizeExample).Testid("ui-calendar-size")
                .Blurb("Xs, Sm, Lg, Xl and Xxl, for the layout around it."),
            UiDocsExample.Key("ui-calendar-static").Heading("Static").File("CalendarStaticExample.cs").Demo(CalendarStaticExample).Testid("ui-calendar-static")
                .Blurb("A calendar to look at, with no month steps."),
            UiDocsExample.Key("ui-calendar-max").Heading("Min/max dates").File("CalendarMaxExample.cs").Demo(CalendarMaxExample).Testid("ui-calendar-max")
                .Blurb("Nothing after today. Flux's \"today\" shorthand is today's DateOnly."),
            UiDocsExample.Key("ui-calendar-unavailable").Heading("Unavailable dates").File("CalendarUnavailableExample.cs").Demo(CalendarUnavailableExample).Testid("ui-calendar-unavailable")
                .Blurb("Yesterday and tomorrow struck through: holidays, booked days."),
            UiDocsExample.Key("ui-calendar-today").Heading("With today shortcut").File("CalendarTodayExample.cs").Demo(CalendarTodayExample).Testid("ui-calendar-today")
                .Blurb("A button that comes back to this month, and picks today once there."),
            UiDocsExample.Key("ui-calendar-header").Heading("Selectable header").File("CalendarHeaderExample.cs").Demo(CalendarHeaderExample).Testid("ui-calendar-header")
                .Blurb("The month and the year in the header are selects."),
            UiDocsExample.Key("ui-calendar-inputs").Heading("With inputs").File("CalendarInputsExample.cs").Demo(CalendarInputsExample).Testid("ui-calendar-inputs")
                .Blurb("A field over the month to type the day into, and a range's start and end over two. A whole date "
                       + "typed is chosen at once; a pick is written into the field."),
            UiDocsExample.Key("ui-calendar-fixed").Heading("Fixed weeks").File("CalendarFixedWeeksExample.cs").Demo(CalendarFixedWeeksExample).Testid("ui-calendar-fixed")
                .Blurb("Always six weeks, so paging never changes the height."),
            UiDocsExample.Key("ui-calendar-start").Heading("Start day").File("CalendarStartDayExample.cs").Demo(CalendarStartDayExample).Testid("ui-calendar-start")
                .Blurb("The week starts on the locale's own first day unless this says otherwise."),
            UiDocsExample.Key("ui-calendar-open").Heading("Open to").File("CalendarOpenToExample.cs").Demo(CalendarOpenToExample).Testid("ui-calendar-open")
                .Blurb("The month it opens on while nothing is chosen: next month, a year on."),
            UiDocsExample.Key("ui-calendar-force").Heading("Force open to").File("CalendarForceOpenToExample.cs").Demo(CalendarForceOpenToExample).Testid("ui-calendar-force")
                .Blurb("ForceOpenTo opens on that month whatever is chosen: today is picked here, and a year on is shown."),
            UiDocsExample.Key("ui-calendar-weeks").Heading("Week numbers").File("CalendarWeekNumbersExample.cs").Demo(CalendarWeekNumbersExample).Testid("ui-calendar-weeks"),
            UiDocsExample.Key("ui-calendar-locale").Heading("Localization").File("CalendarLocaleExample.cs").Demo(CalendarLocaleExample).Testid("ui-calendar-locale")
                .Blurb("The app's culture unless Locale names another: month and weekday names and the first day of the week."),
            UiCalendarBehaviour
        ];
}
