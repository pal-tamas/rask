namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TextVariantsExample : Component
{
    protected override Component? Render() =>
        [
            Ui.Text.Variant(Ui.TextVariant.Strong)["Strong text color"],
            Ui.Text["Default text color"],
            Ui.Text.Subtle["Subtle text color"],
            Ui.Text.Color(Ui.Color.Blue)["Colored text"]
        ];
}
