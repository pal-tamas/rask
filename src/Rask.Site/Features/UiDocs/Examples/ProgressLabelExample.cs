namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ProgressLabelExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-64")[
            Div.Id("progress-upload").Class("mb-3 text-sm font-medium")["Upload progress"],
            Ui.Progress.Value(42).Color(Ui.Color.Blue).Aria("labelledby", "progress-upload"),
            Ui.Text.Sm.Class("mt-3")["Uploading 3 of 7 files..."]
        ];
}
