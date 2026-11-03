using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>
///     A layout that places <c>Ui.ConfirmLeave</c> in its own words, as a whole page: what the confirm-leave page frames.
/// </summary>
/// <remarks>
///     A page of its own because the dialog is one per page: the UI tab's layout already places the default one.
/// </remarks>
[Route("demo/confirm-leave-words")]
public sealed partial class ConfirmLeaveWordsDemoPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets =>
        UiLayoutDemo.Titled("Ui.ConfirmLeave in your own words demo", UiDocsCatalog.ConfirmLeave);

    /// <inheritdoc />
    protected override Component? Render() => ConfirmLeaveWordsExample;
}
