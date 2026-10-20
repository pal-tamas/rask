namespace Rask.Site.Features.UiDocs;

/// <summary>How a brand behaves: where it goes, what its logo can be, and the space it keeps.</summary>
internal sealed partial class UiBrandBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            UiDocsTopic.Key("logo").Heading("An image, or a mark of your own").Paragraphs([
                "`Logo` takes either: `.Logo(\"/logo.png\")` draws the image, and "
                + "`.Logo(Ui.Icon.Name(Ui.IconName.RocketLaunch).Micro)` draws what it is handed, in a box "
                + "`LogoClass` dresses with a fill, an ink and a shape. That is Flux's `logo` prop and its `logo` slot "
                + "in one step. An image's `Alt` is empty unless set, because the name beside it already says it; a "
                + "brand with no `Name` should say it with `Alt`.",
            ]),
            UiDocsTopic.Key("href").Heading("It goes home").Paragraphs([
                "`Href` is `/` when unset. Like every kit component that goes somewhere, a generated route navigates "
                + "inside the app under its path base, and a string is an ordinary link written as given.",
            ]),
            UiDocsTopic.Key("space").Heading("The space after it").Paragraphs([
                "A kit component brings no outer margin, with this exception, part of the brand's shape rather than "
                + "its placement: it carries `me-4`, the gap Flux puts between a brand and the navbar after it. In a "
                + "sidebar the brand is `Ui.SidebarBrand`, a part of the sidebar layout, which also takes a `LogoDark`.",
            ])
        ];
}
