namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ProfileTrailingIconExample : Component
{
    protected override Component? Render() =>
        Ui.Profile.IconTrailing(Ui.IconName.ChevronUpDown).Avatar("/img/avatars/amber.svg").Name("Caleb Porzio");
}
