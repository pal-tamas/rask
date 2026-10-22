using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's date picker and its triggers as C# components: each example with its own code, and its reference.</summary>
[Route("date-picker")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiDatePickerPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.DatePicker.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.DatePicker)
            .Lead("A day or a range picked from a calendar in a popup, for filtering data or scheduling events. For a "
                  + "date far in the past or the future, such as a birthday, a date input serves better.")
            .Api(UiApi.DatePicker)[
            Picking(),
            Ranges(),
            Calendar(),
            UiDatePickerBehaviour
        ];

    private static Component[] Picking() =>
    [
        UiDocsExample.Key("ui-date-picker").Heading("Date picker").File("DatePickerExample.cs").Demo(DatePickerExample).Testid("ui-date-picker")
            .Blurb("A field-shaped button over the calendar: a day closes it on the pick."),
        UiDocsExample.Key("ui-date-picker-bound").Heading("Basic usage").File("DatePickerBoundExample.cs").Demo(DatePickerBoundExample).Testid("ui-date-picker-bound")
            .Blurb("Bound to a DateOnly? that starts on today, as Flux's value does."),
        UiDocsExample.Key("ui-date-picker-input").Heading("Input trigger").File("DatePickerInputExample.cs").Demo(DatePickerInputExample).Testid("ui-date-picker-input")
            .Blurb("Month, day and year typed as one field: a part that is full moves on to the next, and the chevron opens the calendar."),
    ];

    private static Component[] Ranges() =>
    [
        UiDocsExample.Key("ui-date-picker-range").Heading("Range picker").File("DatePickerRangeExample.cs").Demo(DatePickerRangeExample).Testid("ui-date-picker-range")
            .Blurb("Two months and a UiDateRange, closed by the second click."),
        UiDocsExample.Key("ui-date-picker-limits").Heading("Range limits").File("DatePickerRangeLimitsExample.cs").Demo(DatePickerRangeLimitsExample).Testid("ui-date-picker-limits")
            .Blurb("At least three days, and at most ten."),
        UiDocsExample.Key("ui-date-picker-range-inputs").Heading("Range with inputs").File("DatePickerRangeInputsExample.cs").Demo(DatePickerRangeInputsExample).Testid("ui-date-picker-range-inputs")
            .Blurb("A trigger of your own: a field for the start and one for the end, each typed into and each opening the calendar."),
        UiDocsExample.Key("ui-date-picker-with-inputs").Heading("With inputs").File("DatePickerWithInputsExample.cs").Demo(DatePickerWithInputsExample).Testid("ui-date-picker-with-inputs")
            .Blurb("WithInputs() puts the start and the end over the months, to type into: drawn as parts, Flux's custom variant."),
        UiDocsExample.Key("ui-date-picker-native").Heading("Native inputs").File("DatePickerNativeInputsExample.cs").Demo(DatePickerNativeInputsExample).Testid("ui-date-picker-native")
            .Blurb("The browser's own date fields in the same box, Flux's native variant: over the months, and as the trigger itself."),
        UiDocsExample.Key("ui-date-picker-presets").Heading("Presets").File("DatePickerPresetsExample.cs").Demo(DatePickerPresetsExample).Testid("ui-date-picker-presets")
            .Blurb("Today, Yesterday, This Week, Last 7 Days, This Month, Year to Date and All Time, which starts at Min. A preset closes the picker."),
        UiDocsExample.Key("ui-date-picker-preset-list").Heading("Choosing the presets").File("DatePickerPresetListExample.cs").Demo(DatePickerPresetListExample).Testid("ui-date-picker-preset-list")
            .Blurb("Presets lists the ones you name, in your order. With Custom among them, a range that matches no other preset is \"Custom Range\"."),
    ];

    private static Component[] Calendar() =>
    [
        UiDocsExample.Key("ui-date-picker-unavailable").Heading("Unavailable dates").File("DatePickerUnavailableExample.cs").Demo(DatePickerUnavailableExample).Testid("ui-date-picker-unavailable"),
        UiDocsExample.Key("ui-date-picker-today").Heading("With today shortcut").File("DatePickerTodayExample.cs").Demo(DatePickerTodayExample).Testid("ui-date-picker-today"),
        UiDocsExample.Key("ui-date-picker-header").Heading("Selectable header").File("DatePickerHeaderExample.cs").Demo(DatePickerHeaderExample).Testid("ui-date-picker-header"),
        UiDocsExample.Key("ui-date-picker-fixed").Heading("Fixed weeks").File("DatePickerFixedWeeksExample.cs").Demo(DatePickerFixedWeeksExample).Testid("ui-date-picker-fixed"),
        UiDocsExample.Key("ui-date-picker-start").Heading("Start day").File("DatePickerStartDayExample.cs").Demo(DatePickerStartDayExample).Testid("ui-date-picker-start"),
        UiDocsExample.Key("ui-date-picker-open").Heading("Open to").File("DatePickerOpenToExample.cs").Demo(DatePickerOpenToExample).Testid("ui-date-picker-open")
            .Blurb("Where the calendar opens while nothing is chosen; with ForceOpenTo, whatever is chosen."),
        UiDocsExample.Key("ui-date-picker-weeks").Heading("Week numbers").File("DatePickerWeekNumbersExample.cs").Demo(DatePickerWeekNumbersExample).Testid("ui-date-picker-weeks"),
        UiDocsExample.Key("ui-date-picker-locale").Heading("Localization").File("DatePickerLocaleExample.cs").Demo(DatePickerLocaleExample).Testid("ui-date-picker-locale"),
        UiDocsExample.Key("ui-date-picker-clearable").Heading("Clearable").File("DatePickerClearableExample.cs").Demo(DatePickerClearableExample).Testid("ui-date-picker-clearable")
            .Blurb("A button inside the trigger, before its chevron, while a date is chosen, beside a time picker that has one too."),
    ];
}
