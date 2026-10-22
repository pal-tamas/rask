namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatePickerUnavailableExample : Component
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().Date);

    protected override Component? Render() =>
        Div.Class("max-w-xs")[
            Ui.DatePicker.Value(default(DateOnly)).Unavailable([Today.AddDays(-1), Today.AddDays(1)])
        ];
}
