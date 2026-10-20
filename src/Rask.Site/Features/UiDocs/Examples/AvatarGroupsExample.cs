namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class AvatarGroupsExample : Component
{
    private const string Caleb = "/img/avatars/amber.svg";

    private const string Hugo = "/img/avatars/sky.svg";

    private const string Josh = "/img/avatars/emerald.svg";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-6")[
            Ui.AvatarGroup.Key("group")[
                Ui.Avatar.Key("caleb").Src(Caleb).Name("Caleb Porzio"),
                Ui.Avatar.Key("hugo").Src(Hugo).Name("Hugo Sainte-Marie"),
                Ui.Avatar.Key("josh").Src(Josh).Name("Josh Hanley"),
                Ui.Avatar.Key("more")["3+"]
            ],
            // On another ground, each avatar's ring takes that ground's colour.
            Div.Key("ground").Class("rounded-lg bg-zinc-100 p-3 dark:bg-zinc-800")[
                Ui.AvatarGroup.Class("*:ring-zinc-100 dark:*:ring-zinc-800")[
                    Ui.Avatar.Key("ground-caleb").Circle().Lg.Src(Caleb).Name("Caleb Porzio"),
                    Ui.Avatar.Key("ground-hugo").Circle().Lg.Src(Hugo).Name("Hugo Sainte-Marie"),
                    Ui.Avatar.Key("ground-josh").Circle().Lg.Src(Josh).Name("Josh Hanley"),
                    Ui.Avatar.Key("ground-more").Circle().Lg["3+"]
                ]
            ]
        ];
}
