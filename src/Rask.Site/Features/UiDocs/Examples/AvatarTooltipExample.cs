namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class AvatarTooltipExample : Component
{
    protected override Component? Render() =>
        Ui.Avatar.Tooltip("Caleb Porzio").Src("/img/avatars/amber.svg").Name("Caleb Porzio");
}
