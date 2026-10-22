namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatePickerWithInputsExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs")[
            Ui.DatePicker.Range.Value(default(UiDateRange)).WithInputs()
        ];
}
