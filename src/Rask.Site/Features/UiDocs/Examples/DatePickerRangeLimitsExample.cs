namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatePickerRangeLimitsExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-4")[
            Ui.DatePicker.Range.Value(default(UiDateRange)).MinRange(3),
            Ui.DatePicker.Range.Value(default(UiDateRange)).MaxRange(10)
        ];
}
