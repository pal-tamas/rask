using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Heroicons as a C# component: each example with its own code, and its reference.</summary>
[Route("icon")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiIconPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Icon.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Icon)
            .Lead("Every Heroicon, by name, as an inline SVG: outline, solid, mini and micro, sized and "
                  + "coloured with a class. Most of the kit takes an icon as a step and places it itself.")
            .Api(UiApi.Icon)[
            UiDocsExample.Key("ui-icon-intro").Heading("Icon").File("IconExample.cs").Demo(IconExample).Testid("ui-icon-intro")
                .Blurb("Ui.IconName lists them all."),
            UiDocsExample.Key("ui-icon-variants").Heading("Variants").File("IconVariantsExample.cs").Demo(IconVariantsExample).Testid("ui-icon-variants")
                .Blurb("Outline and solid are 24px, mini 20px and micro 16px, each drawn for its size."),
            UiDocsExample.Key("ui-icon-sizes").Heading("Sizes").File("IconSizesExample.cs").Demo(IconSizesExample).Testid("ui-icon-sizes")
                .Blurb("A size-* class overrides the drawing's own size."),
            UiDocsExample.Key("ui-icon-colors").Heading("Colors").File("IconColorsExample.cs").Demo(IconColorsExample).Testid("ui-icon-colors")
                .Blurb("It draws in the text colour, so a text-* class colours it."),
            UiDocsExample.Key("ui-icon-components").Heading("In other components").File("IconInComponentsExample.cs").Demo(IconInComponentsExample).Testid("ui-icon-components")
                .Blurb("Most of the kit takes an icon as a step, and sizes and spaces it itself.")
        ];
}
