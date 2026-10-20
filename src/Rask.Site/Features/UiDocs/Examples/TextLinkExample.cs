namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TextLinkExample : Component
{
    protected override Component? Render() =>
        Ui.Text[
            "Visit our ",
            Ui.Link.Href("https://rask.sh/docs").External()["documentation"],
            " for more information."
        ];
}
