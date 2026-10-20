namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ProfileNameExample : Component
{
    protected override Component? Render() =>
        Ui.Profile.Name("Mara Lind").Avatar("/img/avatars/amber.svg");
}
