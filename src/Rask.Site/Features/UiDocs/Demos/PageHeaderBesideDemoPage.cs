using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>A page whose title carries its status beside it, as a whole page: what the page header page frames.</summary>
/// <remarks>A page of its own for the same reason as <see cref="PageHeaderDemoPage" />: the header is its page's one <c>&lt;h1&gt;</c>.</remarks>
[Route("demo/page-header-beside")]
public sealed partial class PageHeaderBesideDemoPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiLayoutDemo.Titled("Ui.PageHeader with a badge beside its title demo", UiDocsCatalog.PageHeader);

    /// <inheritdoc />
    protected override Component? Render() => PageHeaderBesideExample;
}
