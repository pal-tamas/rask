namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ProfileCircleExample : Component
{
    private const string Mara = "/img/avatars/amber.svg";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-6")[
            Ui.Profile.Key("bare").Chevron(false).Avatar(Mara),
            Ui.Profile.Key("circle").Circle().Chevron(false).Avatar(Mara),
            Ui.Profile.Key("circle-name").Circle().Name("Mara Lind").Avatar(Mara)
        ];
}
