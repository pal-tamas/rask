namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatePickerNativeInputsExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-4")[
            Ui.DatePicker.Range.Value(default(UiDateRange)).WithInputs(Ui.DatePickerInputVariant.Native),
            Ui.DatePicker.Value(default(DateOnly)).Trigger(Ui.DatePickerInput.Native)
        ];
}
