namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class BadgeRoundedExample : Component
{
    protected override Component? Render() =>
        Ui.Badge.Rounded().Icon(Ui.IconName.User)["Users"];
}
