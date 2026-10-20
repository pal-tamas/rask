namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class AvatarExample : Component
{
    protected override Component? Render() =>
        Ui.Avatar.Src("https://unavatar.io/x/calebporzio").Name("Caleb Porzio");
}
