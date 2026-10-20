namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TextSizesExample : Component
{
    protected override Component? Render() =>
        [
            Ui.Text.Lg["Larger text size"],
            Ui.Text["Default text size"],
            Ui.Text.Sm["Smaller text"]
        ];
}
