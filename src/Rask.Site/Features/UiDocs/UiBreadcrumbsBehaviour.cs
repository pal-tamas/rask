namespace Rask.Site.Features.UiDocs;

/// <summary>How breadcrumbs behave: links and the page being shown, the separator, and what a step can hold.</summary>
internal sealed partial class UiBreadcrumbsBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            UiDocsTopic.Key("steps").Heading("Links, and the page you are on").Paragraphs([
                "A step with an `Href` is a link; one without is plain, greyed text, because the page being shown is "
                + "not a link to itself. A generated route navigates inside the app under its path base, and a string "
                + "is an ordinary link written as given. The trail itself is a plain `<div>`, as Flux's is.",
            ]),
            UiDocsTopic.Key("separator").Heading("The separator").Paragraphs([
                "Each step draws the separator after it: a chevron that turns with the reading direction, so it points "
                + "the right way in a right-to-left page, and is not drawn after the last step. "
                + "`Separator(Ui.IconName.Slash)` is Flux's other one, and any icon will do.",
            ]),
            UiDocsTopic.Key("children").Heading("What a step can hold").Paragraphs([
                "A step's words are its children, and an `Icon` can stand in for them. Anything else goes in as a child "
                + "too, which is how a dropdown holding the folded-away steps goes in: "
                + "`Ui.BreadcrumbsItem[Ui.Dropdown[Ui.Button.Ghost.Sm.Icon(Ui.IconName.EllipsisHorizontal), Ui.Navmenu[…]]]`.",
            ])
        ];
}
