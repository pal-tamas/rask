namespace Rask.Site.Features.UiKit;

/// <summary>Flux UI's icon page, example for example: Heroicons by name, in four drawings, any size and any colour.</summary>
public sealed partial class UiKitIconDemo : Component
{
    private static readonly Ui.IconName[] Some =
    [
        Ui.IconName.Bolt, Ui.IconName.Bell, Ui.IconName.Calendar, Ui.IconName.ChartBar, Ui.IconName.Cog6Tooth,
        Ui.IconName.Envelope, Ui.IconName.Heart, Ui.IconName.Home, Ui.IconName.MagnifyingGlass, Ui.IconName.User,
    ];

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Data("testid", "ui-icon")[
            UiKitExample.Key("icon").Heading("Icon").Blurb("Every Heroicon, by name: Ui.IconName lists them all.")[
                Row(Some.Select(name => Ui.Icon.Name(name).Key(name)))
            ],
            UiKitExample.Key("variants").Heading("Variants")
                .Blurb("Outline and solid are 24px, mini 20px and micro 16px, each drawn for its size.")[
                Row(
                    Ui.Icon.Name(Ui.IconName.Bolt).Key("outline"),
                    Ui.Icon.Name(Ui.IconName.Bolt).Key("solid").Solid,
                    Ui.Icon.Name(Ui.IconName.Bolt).Key("mini").Mini,
                    Ui.Icon.Name(Ui.IconName.Bolt).Key("micro").Micro)
            ],
            UiKitExample.Key("sizes").Heading("Sizes").Blurb("A size-* class overrides the drawing's own size.")[
                Row(
                    Ui.Icon.Name(Ui.IconName.Bolt).Key("12").Class("size-12"),
                    Ui.Icon.Name(Ui.IconName.Bolt).Key("8").Class("size-8"),
                    Ui.Icon.Name(Ui.IconName.Bolt).Key("6").Class("size-6"),
                    Ui.Icon.Name(Ui.IconName.Bolt).Key("4").Class("size-4"))
            ],
            UiKitExample.Key("colors").Heading("Colors").Blurb("It draws in the text colour, so a text-* class colours it.")[
                Row(
                    Ui.Icon.Name(Ui.IconName.Bolt).Key("orange").Class("text-orange-500"),
                    Ui.Icon.Name(Ui.IconName.Bolt).Key("emerald").Class("text-emerald-500"),
                    Ui.Icon.Name(Ui.IconName.Bolt).Key("sky").Class("text-sky-500"),
                    Ui.Icon.Name(Ui.IconName.Bolt).Key("zinc").Class("text-zinc-400 dark:text-zinc-500"))
            ],
            UiKitExample.Key("props").Heading("In other components")
                .Blurb("Most of the kit takes an icon as a step, and sizes and spaces it itself.")[
                Row(
                    Ui.Button.Key("button").Icon(Ui.IconName.ArrowDownTray)["Export"],
                    Ui.Badge.Key("badge").Icon(Ui.IconName.Bolt)["Fast"],
                    Ui.Input.Of<string>().Key("input").Icon(Ui.IconName.MagnifyingGlass).Placeholder("Search...")
                        .Class("max-w-56"))
            ]
        ];

    private static Component Row(params IEnumerable<Component?> items) =>
        Div.Class("flex flex-wrap items-center gap-4 text-zinc-800 dark:text-white")[items];
}
