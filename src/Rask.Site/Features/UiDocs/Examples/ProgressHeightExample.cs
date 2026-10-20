namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ProgressHeightExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-64")[
            Ui.Progress.Value(75).Class("h-3").Aria("label", "Battery")
        ];
}
