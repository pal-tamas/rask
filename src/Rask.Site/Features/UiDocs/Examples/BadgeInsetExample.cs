namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class BadgeInsetExample : Component
{
    protected override Component? Render() =>
        [
            Ui.Heading.Lg["Page builder ", Ui.Badge.Color(Ui.Color.Lime).Inset(Ui.Inset.Top | Ui.Inset.Bottom)["New"]],
            Ui.Text.Class("mt-2")["Easily author new pages without leaving your browser."]
        ];
}
