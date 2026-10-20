namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class HeadingLeadingExample : Component
{
    protected override Component? Render() =>
        [
            Ui.Text["Year to date"],
            Ui.Heading.Xl.Class("mb-1")["$7,532.16"],
            Ui.Text.Color(Ui.Color.Green)["15.2%"]
        ];
}
