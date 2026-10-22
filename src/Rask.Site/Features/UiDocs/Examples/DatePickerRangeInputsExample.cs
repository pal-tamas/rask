namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatePickerRangeInputsExample : Component
{
    protected override Component? Render() =>
        Ui.DatePicker.Range.Value(default(UiDateRange)).Trigger(
            Div.Class("flex flex-col gap-6 sm:flex-row sm:gap-4")[
                Ui.DatePickerInput.Key("start").Label("Start"),
                Ui.DatePickerInput.Key("end").Label("End")
            ]);
}
