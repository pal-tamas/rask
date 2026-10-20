namespace Rask.Site.Features.UiKit;

/// <summary>
///     Flux UI's badge page, example for example: a status, a category or a count in any Tailwind colour.
/// </summary>
/// <remarks>
///     The two badges that do something keep their state here: how often the amount was pressed, and which roles are
///     left after their close buttons.
/// </remarks>
public sealed partial class UiKitBadgeDemo : Component
{
    private static readonly Ui.Color?[] Colors =
    [
        null, Ui.Color.Red, Ui.Color.Orange, Ui.Color.Amber, Ui.Color.Yellow, Ui.Color.Lime, Ui.Color.Green,
        Ui.Color.Emerald, Ui.Color.Teal, Ui.Color.Cyan, Ui.Color.Sky, Ui.Color.Blue, Ui.Color.Indigo,
        Ui.Color.Violet, Ui.Color.Purple, Ui.Color.Fuchsia, Ui.Color.Pink, Ui.Color.Rose,
    ];

    private int _amount = 1;
    private readonly List<string> _roles = ["Admin", "Editor", "Billing"];

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Data("testid", "ui-badge")[
            UiKitExample.Key("intro").Heading("Badge")[Row(Ui.Badge.Color(Ui.Color.Lime)["New"])],
            UiKitExample.Key("sizes").Heading("Sizes")[
                Row(Ui.Badge.Sm["Small"], Ui.Badge["Default"], Ui.Badge.Lg["Large"])
            ],
            UiKitExample.Key("icons").Heading("Icons")[
                Row(Ui.Badge.Icon(Ui.IconName.UserCircle)["Users"],
                    Ui.Badge.Icon(Ui.IconName.DocumentText)["Files"],
                    Ui.Badge.IconTrailing(Ui.IconName.VideoCamera)["Videos"])
            ],
            UiKitExample.Key("rounded").Heading("Rounded")[Row(Ui.Badge.Rounded().Icon(Ui.IconName.User)["Users"])],
            UiKitExample.Key("button").Heading("As button").Blurb("A <div> until it is told to be a button.")[
                Row(Ui.Badge
                    .As(Ui.BadgeAs.Button)
                    .Rounded()
                    .Icon(Ui.IconName.Plus)
                    .Lg
                    .Data("testid", "ui-badge-amount")
                    .OnClick(() => { _amount++; })[$"Amount {_amount}"])
            ],
            UiKitExample.Key("close").Heading("With close button").Blurb("A close button inside it makes it removable.")[
                Row([.. _roles.Select(Removable)])
            ],
            UiKitExample.Key("colors").Heading("Colors")[
                Row([.. Colors.Select(color => Ui.Badge.Key(ColorName(color)).Color(color)[ColorName(color)])])
            ],
            UiKitExample.Key("solid").Heading("Solid variant")[
                Row([.. Colors.Select(color => Ui.Badge.Key(ColorName(color)).Solid.Color(color)[ColorName(color)])])
            ],
            UiKitExample.Key("inset").Heading("Inset").Blurb("Takes its padding back, so it sits in a heading's line.")[
                Div.Data("testid", "ui-badge-inset")[
                    Div.Class("text-base font-medium")[
                        "Page builder ",
                        Ui.Badge.Color(Ui.Color.Lime).Inset(Ui.Inset.Top | Ui.Inset.Bottom)["New"]
                    ],
                    Ui.Text.Class("mt-2")["Easily author new pages without leaving your browser."]
                ]
            ]
        ];

    private Component Removable(string role) =>
        Ui.Badge.Key(role)[
            role,
            Ui.BadgeClose.Aria("label", "Remove " + role).OnClick(() => { _roles.Remove(role); })
        ];

    private static string ColorName(Ui.Color? color) => color?.ToString() ?? "Zinc";

    private static Component Row(params Component[] badges) => Div.Class("flex flex-wrap items-end gap-2")[badges];
}
