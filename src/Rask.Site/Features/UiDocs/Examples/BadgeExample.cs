namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class BadgeExample : Component
{
    protected override Component? Render() =>
        Ui.Badge.Color(Ui.Color.Lime)["New"];
}
