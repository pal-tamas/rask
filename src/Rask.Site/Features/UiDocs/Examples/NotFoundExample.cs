namespace Rask.Site.Features.UiDocs.Examples;

// What an address no page answers says, where the page would have been: one sentence, and the way home.
public sealed partial class NotFoundExample : Component
{
    protected override Component? Render() =>
        Ui.NotFound.Home(Routes.UiIntroductionPage());
}
