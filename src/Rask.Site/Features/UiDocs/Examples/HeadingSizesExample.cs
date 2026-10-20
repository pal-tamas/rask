namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class HeadingSizesExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-col gap-4")[
            Ui.Heading["Default"],
            Ui.Heading.Lg["Large"],
            Ui.Heading.Xl["Extra large"],
            Ui.Heading.Xxl["Extra extra large"]
        ];
}
