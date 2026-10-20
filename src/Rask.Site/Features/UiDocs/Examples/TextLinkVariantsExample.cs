namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TextLinkVariantsExample : Component
{
    protected override Component? Render() =>
        [
            Ui.Text[Ui.Link.Href("#")["Default link"]],
            Ui.Text[Ui.Link.Href("#").Ghost["Ghost link"]],
            Ui.Text[Ui.Link.Href("#").Subtle["Subtle link"]],
            Ui.Text[Ui.Link.As(Ui.LinkAs.Button)["Create new account →"]]
        ];
}
