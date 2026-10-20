namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class AvatarGroupsExample : Component
{
    private const string Mara = "/img/avatars/amber.svg";

    private const string Theo = "/img/avatars/sky.svg";

    private const string Nia = "/img/avatars/emerald.svg";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-6")[
            Ui.AvatarGroup.Key("group")[
                Ui.Avatar.Key("mara").Src(Mara).Name("Mara Lind"),
                Ui.Avatar.Key("theo").Src(Theo).Name("Theo Brandt"),
                Ui.Avatar.Key("nia").Src(Nia).Name("Nia Okafor"),
                Ui.Avatar.Key("more")["3+"]
            ],
            // On another ground, each avatar's ring takes that ground's colour.
            Div.Key("ground").Class("rounded-lg bg-zinc-100 p-3 dark:bg-zinc-800")[
                Ui.AvatarGroup.Class("*:ring-zinc-100 dark:*:ring-zinc-800")[
                    Ui.Avatar.Key("ground-mara").Circle().Lg.Src(Mara).Name("Mara Lind"),
                    Ui.Avatar.Key("ground-theo").Circle().Lg.Src(Theo).Name("Theo Brandt"),
                    Ui.Avatar.Key("ground-nia").Circle().Lg.Src(Nia).Name("Nia Okafor"),
                    Ui.Avatar.Key("ground-more").Circle().Lg["3+"]
                ]
            ]
        ];
}
