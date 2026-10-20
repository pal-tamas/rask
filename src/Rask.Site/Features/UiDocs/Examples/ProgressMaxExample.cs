namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ProgressMaxExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-64")[
            Ui.Progress.Value(3).Max(7).Aria("label", "Steps done")
        ];
}
