namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class AvatarTooltipExample : Component
{
    protected override Component? Render() =>
        Ui.Avatar.Tooltip("Mara Lind").Src("/img/avatars/amber.svg").Name("Mara Lind");
}
