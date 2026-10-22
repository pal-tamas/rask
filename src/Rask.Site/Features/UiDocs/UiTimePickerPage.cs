using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's time picker as a C# component: each example with its own code, and its reference.</summary>
[Route("time-picker")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiTimePickerPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.TimePicker.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.TimePicker)
            .Lead("A time picked from a list, for scheduling events or setting appointments. The type it binds says how "
                  + "many: a TimeOnly? for one or none, a collection of TimeOnly for several.")
            .Api(UiApi.TimePicker)[
            UiDocsExample.Key("ui-time-picker").Heading("Time picker").File("TimePickerExample.cs").Demo(TimePickerExample).Testid("ui-time-picker")
                .Blurb("A button over a list of times: the button keeps the focus, the arrows move through the list and Enter picks."),
            UiDocsExample.Key("ui-time-picker-bound").Heading("Basic usage").File("TimePickerBoundExample.cs").Demo(TimePickerBoundExample).Testid("ui-time-picker-bound")
                .Blurb("Bound to a TimeOnly? that starts at 11:30, as Flux's value does."),
            UiDocsExample.Key("ui-time-picker-input").Heading("Input trigger").File("TimePickerInputExample.cs").Demo(TimePickerInputExample).Testid("ui-time-picker-input")
                .Blurb("Hour, minute and the half of the day typed as one field: 9 can only be nine o'clock, so it moves on, and p is the afternoon."),
            UiDocsExample.Key("ui-time-picker-typed").Heading("Without dropdown").File("TimePickerNoDropdownExample.cs").Demo(TimePickerNoDropdownExample).Testid("ui-time-picker-typed")
                .Blurb("The typed trigger with no list under it."),
            UiDocsExample.Key("ui-time-picker-multiple").Heading("Multiple times").File("TimePickerMultipleExample.cs").Demo(TimePickerMultipleExample).Testid("ui-time-picker-multiple")
                .Blurb("Opened on a collection: the list stays open while times are picked."),
            UiDocsExample.Key("ui-time-picker-format").Heading("Time format").File("TimePickerFormatExample.cs").Demo(TimePickerFormatExample).Testid("ui-time-picker-format")
                .Blurb("The culture's own clock unless TwelveHour or TwentyFourHour says otherwise."),
            UiDocsExample.Key("ui-time-picker-interval").Heading("Interval").File("TimePickerIntervalExample.cs").Demo(TimePickerIntervalExample).Testid("ui-time-picker-interval")
                .Blurb("Minutes between the times on offer: 30 unless set."),
            UiDocsExample.Key("ui-time-picker-min-max").Heading("Min/max times").File("TimePickerMinMaxExample.cs").Demo(TimePickerMinMaxExample).Testid("ui-time-picker-min-max")
                .Blurb("Nine to five. Flux's \"now\" shorthand is the time it is: TimeOnly.FromDateTime(…)."),
            UiDocsExample.Key("ui-time-picker-unavailable").Heading("Unavailable times").File("TimePickerUnavailableExample.cs").Demo(TimePickerUnavailableExample).Testid("ui-time-picker-unavailable")
                .Blurb("Times, and stretches of time as a UiTimeRange, listed and disabled: booked slots."),
            UiDocsExample.Key("ui-time-picker-open").Heading("Open to").File("TimePickerOpenToExample.cs").Demo(TimePickerOpenToExample).Testid("ui-time-picker-open")
                .Blurb("Where the list opens while nothing is chosen. Otherwise it opens on the chosen time, or the time it is now."),
            UiDocsExample.Key("ui-time-picker-locale").Heading("Localization").File("TimePickerLocaleExample.cs").Demo(TimePickerLocaleExample).Testid("ui-time-picker-locale")
                .Blurb("The times are written in the app's culture unless Locale names another."),
            UiTimePickerBehaviour
        ];
}
