namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class AvatarSizesExample : Component
{
    private const string Mara = "/img/avatars/amber.svg";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-end gap-6")[
            Ui.Avatar.Key("xl").Xl.Src(Mara).Name("Mara Lind"),
            Ui.Avatar.Key("lg").Lg.Src(Mara).Name("Mara Lind"),
            Ui.Avatar.Key("md").Src(Mara).Name("Mara Lind"),
            Ui.Avatar.Key("sm").Sm.Src(Mara).Name("Mara Lind"),
            Ui.Avatar.Key("xs").Xs.Src(Mara).Name("Mara Lind")
        ];
}
