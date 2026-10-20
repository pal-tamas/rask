namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class AvatarAsButtonExample : Component
{
    private const string Caleb = "/img/avatars/amber.svg";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-6")[
            Ui.Avatar.Key("button").As(Ui.AvatarAs.Button).Src(Caleb).Name("Caleb Porzio"),
            Ui.Avatar.Key("link").Href("https://x.com/calebporzio").Src(Caleb).Name("Caleb Porzio")
        ];
}
