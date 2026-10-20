namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ProgressExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-64")[
            Ui.Progress.Value(75).Aria("label", "Sync")
        ];
}
