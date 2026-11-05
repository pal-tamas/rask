using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>
///     The kit's page header — not a Flux component, an addition the owner approved — its examples with their code, and
///     its reference.
/// </summary>
/// <remarks>
///     The header is its page's one <c>&lt;h1&gt;</c>, so each example is a page of its own, framed: drawn here, it would be
///     a second <c>&lt;h1&gt;</c> beside this page's title.
/// </remarks>
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
            UiDocsExample.Key("ui-page-header").Heading("A list page").File("PageHeaderExample.cs")
                .Demo(UiLayoutPreview.Demo(Routes.PageHeaderDemoPage()).Title("A list page opened by Ui.PageHeader")).Testid("ui-page-header")
                .Blurb("Heading(\"Suppliers\") is the page's h1; Subheading is the line under it and Breadcrumbs the trail "
                       + "above. The children are the actions, at the end of the row, 8px apart; on a phone the title takes "
                       + "the whole line and they go under it. The header keeps no room around itself: the page puts "
                       + "Class(\"mb-6\") on it. A page of its own, framed, because the header is its page's one h1."),
            UiDocsExample.Key("ui-page-header-title").Heading("A title alone").File("PageHeaderTitleExample.cs")
                .Demo(UiLayoutPreview.Demo(Routes.PageHeaderTitleDemoPage()).Title("A form page opened by Ui.PageHeader")).Testid("ui-page-header-title")
                .Blurb("A form's page needs only its title, and the room under it is the page's: mb-8 here.")
        ];
}
