using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>A form's page opened by <c>Ui.PageHeader</c> with its title alone, as a whole page: what the page header page frames.</summary>
/// <remarks>A page of its own for the same reason as <see cref="PageHeaderDemoPage" />: the header is its page's one <c>&lt;h1&gt;</c>.</remarks>
[Route("demo/page-header-title")]
public sealed partial class PageHeaderTitleDemoPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiLayoutDemo.Titled("Ui.PageHeader on a form page demo", UiDocsCatalog.PageHeader);

    /// <inheritdoc />
    protected override Component? Render() => PageHeaderTitleExample;
}
