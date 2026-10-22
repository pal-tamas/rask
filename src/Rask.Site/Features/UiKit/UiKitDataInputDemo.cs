namespace Rask.Site.Features.UiKit;

/// <summary>
///     The kit's form controls that have no page of their own in the UI tab yet: Flux UI's composer, autocomplete,
///     pillbox, toggle, phone, calendar, date picker, time picker, color picker and file upload, example by example.
/// </summary>
public sealed partial class UiKitDataInputDemo : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
    [
        ComposerSection(),
        AutocompleteSection(),
        PillboxSection(),
        PillboxComboboxSection(),
        ToggleSection(),
        PhoneSection(),
        CalendarSection(),
        DatePickerSection(),
        TimePickerSection(),
        UiKitColorPickerDemo,
        FileUploadSection()
    ];

    private static Component ToggleSection() =>
        Section(
            "Toggle",
            "Flux's toggle: a setting turned on or off from a compact button — an icon that fills in when it is on. "
            + "Space and Enter turn it, a tooltip names it, and a bound one that saves shows a spinner while it waits.",
            UiKitToggleDemo.Key("toggles"));

    private static AttrBag Testid(string value) => new("testid", value);

    private static Component Section(string heading, string blurb, Component body) =>
        Div.Key(heading).Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            body
        ];
}
