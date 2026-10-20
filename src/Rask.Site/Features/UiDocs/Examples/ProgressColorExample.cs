namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ProgressColorExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-64")[
            Ui.Progress.Value(75).Color(Ui.Color.Purple).Aria("label", "Quota")
        ];
}
