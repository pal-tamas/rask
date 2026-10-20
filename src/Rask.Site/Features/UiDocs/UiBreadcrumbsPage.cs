using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's breadcrumbs as a C# component: each example with its own code, and its reference.</summary>
[Route("breadcrumbs")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiBreadcrumbsPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Breadcrumbs.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Breadcrumbs)
            .Lead("The trail from the top of the app to the page being shown. Every step but the last is a link; "
                  + "the last is the page itself, in grey.")
            .Api(UiApi.Breadcrumbs)[
            UiDocsExample.Key("ui-breadcrumbs").Heading("Breadcrumbs").File("BreadcrumbsExample.cs").Demo(BreadcrumbsExample).Testid("ui-breadcrumbs")
                .Blurb("A chevron between the steps, and none after the last."),
            UiDocsExample.Key("ui-breadcrumbs-slashes").Heading("With slashes").File("BreadcrumbsSlashesExample.cs").Demo(BreadcrumbsSlashesExample).Testid("ui-breadcrumbs-slashes")
                .Blurb("Separator(Ui.IconName.Slash) on each step."),
            UiDocsExample.Key("ui-breadcrumbs-icon").Heading("Icon").File("BreadcrumbsIconExample.cs").Demo(BreadcrumbsIconExample).Testid("ui-breadcrumbs-icon")
                .Blurb("An icon in place of a step's words."),
            UiDocsExample.Key("ui-breadcrumbs-ellipsis").Heading("With ellipsis").File("BreadcrumbsEllipsisExample.cs").Demo(BreadcrumbsEllipsisExample).Testid("ui-breadcrumbs-ellipsis")
                .Blurb("Steps folded away under an ellipsis."),
            UiDocsExample.Key("ui-breadcrumbs-dropdown").Heading("With ellipsis dropdown").File("BreadcrumbsDropdownExample.cs").Demo(BreadcrumbsDropdownExample).Testid("ui-breadcrumbs-dropdown")
                .Blurb("The folded steps in a menu: a dropdown is a step's child like any other."),
            UiBreadcrumbsBehaviour
        ];
}
