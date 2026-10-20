namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class AvatarSizesExample : Component
{
    private const string Caleb = "/img/avatars/amber.svg";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-end gap-6")[
            Ui.Avatar.Key("xl").Xl.Src(Caleb).Name("Caleb Porzio"),
            Ui.Avatar.Key("lg").Lg.Src(Caleb).Name("Caleb Porzio"),
            Ui.Avatar.Key("md").Src(Caleb).Name("Caleb Porzio"),
            Ui.Avatar.Key("sm").Sm.Src(Caleb).Name("Caleb Porzio"),
            Ui.Avatar.Key("xs").Xs.Src(Caleb).Name("Caleb Porzio")
        ];
}
