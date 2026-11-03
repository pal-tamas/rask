using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>A layout that places <c>Ui.Confirm</c> in its own words, as a whole page: what the confirm page frames.</summary>
/// <remarks>
///     A page of its own because the dialog is one per page: the UI tab's layout already places the default one.
/// </remarks>
[Route("demo/confirm-words")]
public sealed partial class ConfirmWordsDemoPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiLayoutDemo.Titled("Ui.Confirm in your own words demo", UiDocsCatalog.Confirm);

    /// <inheritdoc />
    protected override Component? Render() => ConfirmWordsExample;
}
