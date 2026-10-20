namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class AvatarAsButtonExample : Component
{
    private const string Mara = "/img/avatars/amber.svg";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-6")[
            Ui.Avatar.Key("button").As(Ui.AvatarAs.Button).Src(Mara).Name("Mara Lind"),
            Ui.Avatar.Key("link").Href("#").Src(Mara).Name("Mara Lind")
        ];
}
