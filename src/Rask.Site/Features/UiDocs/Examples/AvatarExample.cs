namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class AvatarExample : Component
{
    protected override Component? Render() =>
        Ui.Avatar.Src("/img/avatars/amber.svg").Name("Caleb Porzio");
}
