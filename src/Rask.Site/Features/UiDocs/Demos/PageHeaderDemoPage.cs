using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>A list page opened by <c>Ui.PageHeader</c>, as a whole page: what the page header page frames.</summary>
/// <remarks>
///     A page of its own because the header is its page's one <c>&lt;h1&gt;</c>: drawn in the docs page, it would be a
///     second one beside that page's title. Its canonical is the page header page, so it stays out of the sitemap.
/// </remarks>
[Route("demo/page-header")]
public sealed partial class PageHeaderDemoPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiLayoutDemo.Titled("Ui.PageHeader on a list page demo", UiDocsCatalog.PageHeader);

    /// <inheritdoc />
    protected override Component? Render() => PageHeaderExample;
}
