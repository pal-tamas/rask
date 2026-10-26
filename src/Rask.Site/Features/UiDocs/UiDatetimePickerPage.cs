using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's datetime picker as C# components: each example with its own code, and its reference.</summary>
[Route("datetime-picker")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiDatetimePickerPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.DatetimePicker.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.DatetimePicker)
            .Lead("A date and a time picked together from one field, for scheduling events, publishing posts, setting "
                  + "reminders or booking a window of time. For a date alone or a time alone, the date picker and the time "
                  + "picker serve better.")
            .Api(UiApi.DatetimePicker)[
            Picking(),
            Times(),
            Limits(),
            Shapes(),
            UiDatetimePickerBehaviour
        ];

    private static Component[] Picking() =>
    [
        UiDocsExample.Key("ui-datetime-picker").Heading("Datetime picker").File("DatetimePickerExample.cs").Demo(DatetimePickerExample).Testid("ui-datetime-picker")
            .Blurb("A day from the calendar and a time from the list beside it: the time closes it, a day alone changes the value at once."),
        UiDocsExample.Key("ui-datetime-picker-bound").Heading("Basic usage").File("DatetimePickerBoundExample.cs").Demo(DatetimePickerBoundExample).Testid("ui-datetime-picker-bound")
            .Blurb("Bound to a DateTime?: a wall-clock time, as a datetime-local field holds it."),
        UiDocsExample.Key("ui-datetime-picker-range").Heading("Range picker").File("DatetimePickerRangeExample.cs").Demo(DatetimePickerRangeExample).Testid("ui-datetime-picker-range")
            .Blurb("A UiDateTimeRange: each end gets its own time, and Select range applies it."),
        UiDocsExample.Key("ui-datetime-picker-limits").Heading("Range limits").File("DatetimePickerRangeLimitsExample.cs").Demo(DatetimePickerRangeLimitsExample).Testid("ui-datetime-picker-limits")
            .Blurb("At most seven days, the start and the end days included."),
    ];

    private static Component[] Times() =>
    [
        UiDocsExample.Key("ui-datetime-picker-input").Heading("Time input").File("DatetimePickerInputExample.cs").Demo(DatetimePickerInputExample).Testid("ui-datetime-picker-input")
            .Blurb("An exact time typed in parts, with the list as suggestions; for a moment and for a range."),
        UiDocsExample.Key("ui-datetime-picker-interval").Heading("Interval").File("DatetimePickerIntervalExample.cs").Demo(DatetimePickerIntervalExample).Testid("ui-datetime-picker-interval")
            .Blurb("A time every 15 minutes; 30 when unset."),
        UiDocsExample.Key("ui-datetime-picker-default").Heading("Default time").File("DatetimePickerDefaultTimeExample.cs").Demo(DatetimePickerDefaultTimeExample).Testid("ui-datetime-picker-default")
            .Blurb("Filled in as soon as a day is picked; for a range, the start's and the end's."),
    ];

    private static Component[] Limits() =>
    [
        UiDocsExample.Key("ui-datetime-picker-min-max").Heading("Min/max").File("DatetimePickerMinMaxExample.cs").Demo(DatetimePickerMinMaxExample).Testid("ui-datetime-picker-min-max")
            .Blurb("From now to two weeks on at 5 PM: the days outside are off, and on the first and last day so are the times outside."),
        UiDocsExample.Key("ui-datetime-picker-min-max-times").Heading("Min/max times").File("DatetimePickerMinMaxTimesExample.cs").Demo(DatetimePickerMinMaxTimesExample).Testid("ui-datetime-picker-min-max-times")
            .Blurb("Any future time between 9 AM and 5 PM: the times outside the window are not listed at all."),
        UiDocsExample.Key("ui-datetime-picker-unavailable").Heading("Unavailable dates and times").File("DatetimePickerUnavailableExample.cs").Demo(DatetimePickerUnavailableExample).Testid("ui-datetime-picker-unavailable")
            .Blurb("A whole day, a stretch of every day, and times of one day only. A stretch includes both its ends."),
        UiDocsExample.Key("ui-datetime-picker-format").Heading("Time format").File("DatetimePickerFormatExample.cs").Demo(DatetimePickerFormatExample).Testid("ui-datetime-picker-format"),
    ];

    private static Component[] Shapes() =>
    [
        UiDocsExample.Key("ui-datetime-picker-clearable").Heading("Clearable").File("DatetimePickerClearableExample.cs").Demo(DatetimePickerClearableExample).Testid("ui-datetime-picker-clearable"),
        UiDocsExample.Key("ui-datetime-picker-confirmation").Heading("With confirmation").File("DatetimePickerConfirmationExample.cs").Demo(DatetimePickerConfirmationExample).Testid("ui-datetime-picker-confirmation")
            .Blurb("Nothing is written until Select; Cancel, Escape and a press outside drop the choice."),
        UiDocsExample.Key("ui-datetime-picker-trigger").Heading("Custom trigger").File("DatetimePickerTriggerExample.cs").Demo(DatetimePickerTriggerExample).Testid("ui-datetime-picker-trigger"),
        UiDocsExample.Key("ui-datetime-picker-calendar").Heading("Calendar options").File("DatetimePickerCalendarExample.cs").Demo(DatetimePickerCalendarExample).Testid("ui-datetime-picker-calendar")
            .Blurb("The date picker's own: the today shortcut, a selectable header, week numbers, the start day and fixed weeks."),
        UiDocsExample.Key("ui-datetime-picker-open").Heading("Open to").File("DatetimePickerOpenToExample.cs").Demo(DatetimePickerOpenToExample).Testid("ui-datetime-picker-open")
            .Blurb("A moment to open on while nothing is chosen, or only a time: this month, the list scrolled to it."),
        UiDocsExample.Key("ui-datetime-picker-locale").Heading("Localization").File("DatetimePickerLocaleExample.cs").Demo(DatetimePickerLocaleExample).Testid("ui-datetime-picker-locale"),
        UiDocsExample.Key("ui-datetime-picker-separate").Heading("Separate date and time").File("DatetimePickerSeparateExample.cs").Demo(DatetimePickerSeparateExample).Testid("ui-datetime-picker-separate")
            .Blurb("A date picker and a time picker side by side, for a date and a time kept apart."),
    ];
}
