namespace Rask;

/// <summary>
///     Where a picker's popup puts the focus as it opens: on nothing, as Flux UI's does.
/// </summary>
/// <remarks>
///     Measured on Flux's date picker (2026-11-03, flux parity-focus): opened by a press or by Enter,
///     focus goes to an empty element at the top of the popup, which is then taken away, so it rests on the page —
///     no control is lit and no ring is drawn, in Chromium and in WebKit, whatever an application's own stylesheet
///     says of <c>:focus-visible</c>. The first Tab still starts where the element was and lands in the calendar.
///     Here the element is in the markup with <c>autofocus</c>, which the browser honours as the popover opens,
///     and ui.css takes it away once it has done that, as it does Ui.Modal's.
/// </remarks>
internal abstract partial class UiPickerFocusPlaceholder : global::Rask.Core.RaskMarkup
{
    private static readonly UiPartMarker Marker = new("ui-focus-placeholder");

    internal static Component Render() =>
        Div.Data(Marker.With(null)).TabIndex(0).Autofocus(true);
}
