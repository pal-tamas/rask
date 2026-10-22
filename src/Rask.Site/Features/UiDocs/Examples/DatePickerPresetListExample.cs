namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatePickerPresetListExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs")[
            Ui.DatePicker.Range.Value(default(UiDateRange)).Min(new DateOnly(2012, 1, 1)).Presets([
                Ui.DateRangePreset.Today, Ui.DateRangePreset.Yesterday, Ui.DateRangePreset.ThisWeek, Ui.DateRangePreset.Last7Days,
                Ui.DateRangePreset.ThisMonth, Ui.DateRangePreset.YearToDate, Ui.DateRangePreset.AllTime, Ui.DateRangePreset.Custom,
            ])
        ];
}
