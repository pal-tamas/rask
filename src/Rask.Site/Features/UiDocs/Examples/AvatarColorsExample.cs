namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class AvatarColorsExample : Component
{
    // Flux's seventeen hues, in its order.
    protected override Component? Render() =>
        Div.Class("flex flex-wrap gap-2")[
            Enum.GetValues<Ui.Color>().Take(17).Select(Component (color) =>
                Ui.Avatar.Key(color.ToString()).Name("Caleb Porzio").Color(color))
        ];
}
