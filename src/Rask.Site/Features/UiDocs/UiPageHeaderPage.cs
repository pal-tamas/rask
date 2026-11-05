using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>
///     The kit's page header — not a Flux component, an addition the owner approved — its examples with their code, and
///     its reference.
/// </summary>
[Route("page-header")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiPageHeaderPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.PageHeader.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.PageHeader)
            .Lead("The row a page opens with: its title, a line under it, what the page can do at the end of the row, and the "
                  + "trail above. Not one of Flux UI's components: the kit's own, laid out as Flux's demos lay out a heading "
                  + "and its actions, with Flux's heading, text, breadcrumbs and buttons.")
            .Api(UiApi.PageHeader)[
            UiDocsExample.Key("ui-page-header").Heading("A list page").File("PageHeaderExample.cs").Demo(PageHeaderExample).Testid("ui-page-header")
                .Blurb("Heading(\"Suppliers\") is the page's h1; Subheading is the line under it and Breadcrumbs the trail "
                       + "above. The children are the actions, at the end of the row, 8px apart; on a phone the title takes "
                       + "the whole line and they go under it."),
            UiDocsExample.Key("ui-page-header-title").Heading("A title alone").File("PageHeaderTitleExample.cs").Demo(PageHeaderTitleExample).Testid("ui-page-header-title")
                .Blurb("A form's page needs only its title. The 24px under the header weighs nothing, so Class(\"mb-0\") "
                       + "takes it away.")
        ];
}
