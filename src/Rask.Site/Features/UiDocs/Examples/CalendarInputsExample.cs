namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalendarInputsExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-start gap-8")[
            Ui.Calendar.Value(default(DateOnly)).WithInputs().Class("w-fit"),
            Ui.Calendar.Range.Value(default(UiDateRange)).WithInputs().Class("w-fit")
        ];
}
