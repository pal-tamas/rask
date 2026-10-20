namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ButtonLinkExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap gap-3")[
            Ui.Button.Href("https://google.com").IconTrailing(Ui.IconName.ArrowUpRight)["Visit Google"],
            Ui.Button.Href(PageMeta.LinkTo(Routes.UiBadgePage()))["See the badge"],
            Ui.Button.Ghost.Href("https://github.com/pal-tamas/rask").Attributes(("target", "_blank"), ("rel", "noopener noreferrer"))["GitHub"]
        ];
}
