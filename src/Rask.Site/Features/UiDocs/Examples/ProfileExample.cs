namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ProfileExample : Component
{
    protected override Component? Render() =>
        Ui.Profile.Avatar("/img/avatars/amber.svg");
}
