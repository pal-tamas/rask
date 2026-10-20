using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's badge as a C# component: each example with its own code, and its reference.</summary>
[Route("badge")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiBadgePage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Badge.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Badge)
            .Lead("A status, a category or a count, in any Tailwind colour. A badge is a <div> until it is told "
                  + "to be a button, and a close button inside it makes it removable.")
            .Api(UiApi.Badge)[
            UiDocsExample.Key("ui-badge-intro").Heading("Badge").File("BadgeExample.cs").Demo(BadgeExample).Testid("ui-badge-intro"),
            UiDocsExample.Key("ui-badge-sizes").Heading("Sizes").File("BadgeSizesExample.cs").Demo(BadgeSizesExample).Testid("ui-badge-sizes"),
            UiDocsExample.Key("ui-badge-icons").Heading("Icons").File("BadgeIconsExample.cs").Demo(BadgeIconsExample).Testid("ui-badge-icons")
                .Blurb("An icon before the words, or after them."),
            UiDocsExample.Key("ui-badge-rounded").Heading("Rounded").File("BadgeRoundedExample.cs").Demo(BadgeRoundedExample).Testid("ui-badge-rounded"),
            UiDocsExample.Key("ui-badge-button").Heading("As button").File("BadgeButtonExample.cs").Demo(BadgeButtonExample).Testid("ui-badge-button")
                .Blurb("A press runs C#: the count is a field on the example."),
            UiDocsExample.Key("ui-badge-close").Heading("With close button").File("BadgeCloseExample.cs").Demo(BadgeCloseExample).Testid("ui-badge-close")
                .Blurb("A Ui.BadgeClose inside it makes it removable; removing is the page's."),
            UiDocsExample.Key("ui-badge-colors").Heading("Colors").File("BadgeColorsExample.cs").Demo(BadgeColorsExample).Testid("ui-badge-colors"),
            UiDocsExample.Key("ui-badge-solid").Heading("Solid variant").File("BadgeSolidExample.cs").Demo(BadgeSolidExample).Testid("ui-badge-solid"),
            UiDocsExample.Key("ui-badge-inset").Heading("Inset").File("BadgeInsetExample.cs").Demo(BadgeInsetExample).Testid("ui-badge-inset")
                .Blurb("Takes its padding back, so it sits in a heading's line without making it taller.")
        ];
}
