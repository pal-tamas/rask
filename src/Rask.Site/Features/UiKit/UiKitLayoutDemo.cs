namespace Rask.Site.Features.UiKit;

/// <summary>
///     The kit's layout pieces: a flyout, avatars and the application layout.
/// </summary>
public sealed partial class UiKitLayoutDemo : Component
{
    private bool _drawerOpen;

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        DrawerSection(),
        AvatarSection(),
        ApplicationLayoutSection()
    ];

    private Component DrawerSection() =>
        Section(
            "Flyout navigation",
            "What a drawer was: Ui.Modal as a flyout from the left, with the page owning whether it is open. "
            + "C# opens it, and OnClose tells the page the reader closed it — the corner button, Escape or a "
            + "click outside.",
            Div.Data(Testid("ui-drawer")).Class("flex flex-col items-start gap-2")[
                Ui.Button.Sm.OnClick(() => { _drawerOpen = true; })["Open the drawer"],
                P.Class("text-sm text-ui-muted").Data(Testid("ui-drawer-state"))[
                    _drawerOpen ? "The page knows it is open." : "The page knows it is closed."
                ],
                Ui.Modal
                    .Flyout()
                    .Left
                    .Open(_drawerOpen)
                    .OnClose(() => { _drawerOpen = false; })[
                    Ul.Class("menu w-56 p-0")[
                        Li.Key("a")[A.Href("#")["Overview"]],
                        Li.Key("b")[A.Href("#")["Queues"]],
                        Li.Key("c")[A.Href("#")["Logs"]]
                    ]
                ]
            ]);

    private static Component AvatarSection() =>
        Section(
            "Avatar",
            "A picture of the account, or its initials when it has none.",
            Div.Data(Testid("ui-layout-rest")).Class("flex flex-wrap items-center gap-6")[
                Ui.Avatar.Key("a").Src("/img/rask-mark.svg").Alt("The Rask mark").Circle().Lg,
                // No picture: the monogram stands in. Most accounts have none, and a broken image is
                // worse than two letters — the NAME is still what a screen reader announces.
                Ui.Avatar.Key("a2").Name("Ada Lovelace").Lg
            ]);

    private static Component ApplicationLayoutSection() =>
        Section(
            "Application layout — header, sidebar, main",
            "Flux UI's two layouts. Ui.Header, Ui.Sidebar and Ui.Main are siblings, and whatever holds the main — "
            + "the body, or this box — becomes the grid: the header across the top, the sidebar down the side. "
            + "A layout is a whole page, so each has a demo of its own: there the sidebar slides over the page on "
            + "a phone and narrows to a rail of icons on a desktop, with no script. The current item says so with "
            + "aria-current, worked out from the route.",
            Div.Data(Testid("ui-app-layout")).Class("grid gap-6")[
                Div.Class("flex flex-wrap gap-3")[
                    Ui.Button.Key("sidebar-demo").Href(PageMeta.LinkTo(Routes.SidebarLayoutDemoPage()))["Open the sidebar layout"],
                    Ui.Button.Key("header-demo").Href(PageMeta.LinkTo(Routes.HeaderLayoutDemoPage()))["Open the header layout"]
                ],
                // Not collapsible here: a sidebar that slides over the page is pinned to the viewport, not to a box.
                Div.Data(Testid("ui-layout-box")).Class("h-96 overflow-hidden rounded-xl border border-zinc-200 bg-white dark:border-zinc-700 dark:bg-zinc-800")[
                    Ui.Sidebar.Key("side").Class("border-r border-zinc-200 bg-zinc-50 dark:border-zinc-700 dark:bg-zinc-900")[
                        Ui.SidebarHeader.Key("head")[
                            Ui.SidebarBrand.Key("brand").Name("Rask").Logo("/img/rask-mark.svg")
                                .Href(PageMeta.LinkTo(Routes.UiKitLayoutPage()))
                        ],
                        Ui.SidebarNav.Key("nav")[
                            Ui.SidebarItem.Key("layout").Href(PageMeta.LinkTo(Routes.UiKitLayoutPage()))
                                .Icon(Ui.IconName.BookOpen)["Layout"],
                            Ui.SidebarItem.Key("actions").Href(PageMeta.LinkTo(Routes.UiKitActionsPage()))
                                .Icon(Ui.IconName.Sparkles).Badge("5")["Actions"],
                            Ui.SidebarGroup.Key("more").Heading("More").Expandable()[
                                Ui.SidebarItem.Key("feedback").Href(PageMeta.LinkTo(Routes.UiKitFeedbackPage()))["Feedback"],
                                Ui.SidebarItem.Key("navigation").Href(PageMeta.LinkTo(Routes.UiKitNavigationPage()))["Navigation"]
                            ]
                        ],
                        Ui.SidebarSpacer.Key("gap"),
                        Ui.SidebarProfile.Key("me").Name("Ada Lovelace")
                    ],
                    Ui.Header.Key("bar").Class("border-b border-zinc-200 dark:border-zinc-700")[
                        Span.Class("text-sm font-medium")["Orders"],
                        Ui.Spacer.Key("bar-gap"),
                        Ui.Button.Key("new").Sm["New order"]
                    ],
                    Ui.Main.Key("main")[
                        Ui.Heading.Key("mh").Level(3).Lg["Orders"],
                        Ui.Text.Key("msh").Subtle["Everything placed in the last 30 days."]
                    ]
                ],
                Div.Class("flex flex-col gap-4")[
                    Div.Data(Testid("ui-spacer-row")).Class("flex items-center gap-2 rounded-xl border border-base-300 p-2")[
                        Ui.Button.Key("left").Ghost.Sm["Rask"],
                        Ui.Separator.Key("bar-sep").Vertical().Subtle.Class("my-1"),
                        Ui.Button.Key("docs").Ghost.Sm["Docs"],
                        Ui.Spacer.Key("spacer"),
                        Ui.Button.Sm.Key("right")["Sign in"]
                    ],
                    Div[
                        Ui.Heading.Key("h").Level(3).Lg["Orders"],
                        Ui.Text.Key("sh").Class("mt-2")["Everything placed in the last 30 days."]
                    ],
                    Ui.Separator.Key("then").Text("then"),
                    Ui.Text.Key("t")["Body copy in the kit's scale. ", Ui.Text.Key("strong").Inline().Variant(Ui.TextVariant.Strong)["Strong"],
                        " for what matters, ", Ui.Text.Key("subtle").Inline().Subtle["subtle"], " for what can be skipped."]
                ]
            ]);

    private static AttrBag Testid(string value) => new("testid", value);

    private static Component Section(string heading, string blurb, Component body) =>
        Div.Key(heading).Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            body
        ];
}
