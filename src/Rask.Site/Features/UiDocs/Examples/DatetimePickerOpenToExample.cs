namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatetimePickerOpenToExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-4")[
            Ui.DatetimePicker.Value(default(DateTime)).OpenTo(new DateTime(2026, 12, 1, 9, 0, 0, DateTimeKind.Unspecified)),
            Ui.DatetimePicker.Value(default(DateTime)).OpenTo(new TimeOnly(9, 0))
        ];
}
