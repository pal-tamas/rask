namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class HeadingLevelExample : Component
{
    protected override Component? Render() =>
        [
            Ui.Heading.Level(3)["User profile"],
            Ui.Text.Class("mt-2")["This information will be displayed publicly."]
        ];
}
