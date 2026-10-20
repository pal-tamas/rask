namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ProfileNameExample : Component
{
    protected override Component? Render() =>
        Ui.Profile.Name("Caleb Porzio").Avatar("/img/avatars/amber.svg");
}
