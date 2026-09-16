namespace Rask.Site.Features.UiKit;

/// <summary>
///     daisyUI's Actions category, drawn with the kit and driven by this component's own state.
/// </summary>
/// <remarks>
///     Every interactive part here holds its state in a plain field and re-renders through Rask's diff.
///     That is the point of the category: the dropdown's open state, the dialog's, and the swap's face
///     are all values C# can read, set and persist, which is exactly what the CSS-only versions of these
///     components could not offer.
/// </remarks>
public sealed partial class UiKitActionsDemo : Component
{
    private bool _menuOpen;
    private bool _confirming;
    private bool _muted;
    private UiThemeName _theme = UiThemeName.Light;
    private string _lastAction = "nothing yet";
    private int _saves;
    private string _sort = "name";
    private List<string> _filters = ["open"];
    private bool _showArchived;
    private int _steps;

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        Section(
            "Button",
            "Colour, fill and size are three independent axes and compose, so an outlined error button "
            + "needs no member of its own.",
            Div.Data(Testid("ui-button")).Class("flex flex-wrap items-center gap-2")[
                UiButton.Key("solid").Tone(UiTone.Primary)["Primary"],
                UiButton.Key("outline").Tone(UiTone.Error).Variant(UiVariant.Outline)["Outline"],
                UiButton.Key("soft").Tone(UiTone.Success).Variant(UiVariant.Soft)["Soft"],
                UiButton.Key("dash").Tone(UiTone.Warning).Variant(UiVariant.Dash)["Dash"],
                UiButton.Key("ghost").Variant(UiVariant.Ghost)["Ghost"],
                UiButton.Key("link").Variant(UiVariant.Link)["Link"],
                UiButton.Key("wide").Wide(true)["Wide"],
                UiButton.Key("circle").AccessibleLabel("Close").Circle(true)[UiIcon.Name(UiIconName.Close)],
                UiButton.Key("square").AccessibleLabel("Add").Square(true)[UiIcon.Name(UiIconName.Plus)],
                UiButton.Key("disabled").Disabled(true)["Disabled"]
            ]),

        Section(
            "Button — waiting on its handler",
            "No property to set. A button whose handler is still running after 200 ms shows a spinner at the "
            + "same width, tells a screen reader it is busy, and drops a second press until the first is done. "
            + "Loading(false) opts a stepper out, so its presses queue.",
            Div.Data(Testid("ui-button-loading")).Class("flex flex-wrap items-center gap-3")[
                UiButton.Key("slow-save").Tone(UiTone.Primary).OnClick(async () =>
                {
                    await Task.Delay(1500);
                    _saves++;
                })["Save"],
                UiButton.Key("stepper").Loading(false).OnClick(async () =>
                {
                    await Task.Delay(400);
                    _steps++;
                })[UiIcon.Name(UiIconName.Plus), "Step"],
                Span.Data(Testid("ui-button-loading-count")).Class("text-sm text-ui-muted")[
                    $"Saved {_saves} time{(_saves == 1 ? "" : "s")} · stepped {_steps}"
                ]
            ]),

        Section(
            "Button and link — going somewhere",
            "Given a generated route, a button or a link is an <a> the runtime routes inside the app, so the "
            + "page changes without reloading. A plain string stays an ordinary link, for a URL that leaves.",
            Div.Data(Testid("ui-button-route")).Class("flex flex-wrap items-center gap-3")[
                UiButton.Key("to-navigation").Tone(UiTone.Primary).Variant(UiVariant.Outline)
                    .Href(PageMeta.LinkTo(Routes.UiKitNavigationPage()))["Navigation components"],
                UiLink.Key("to-data-display").Href(PageMeta.LinkTo(Routes.UiKitDataDisplayPage()))
                    .Text("Data display components"),
                UiButton.Key("to-github").Variant(UiVariant.Ghost)
                    .Href("https://github.com/pal-tamas/rask").NewTab(true)["GitHub"]
            ]),

        Section(
            "Dropdown",
            "A popover menu with Flux UI's keyboard: the arrows move a cursor, Home and End jump, a letter "
            + "jumps to the next item starting with it, Right opens a submenu and Left closes it, Enter picks, "
            + "Escape and Tab leave. A pointer crossing diagonally into a submenu keeps it open — the safe "
            + "triangle. Open is nullable: unset leaves it to the reader, true and false hand it to this page.",
            Div.Data(Testid("ui-dropdown")).Class("flex flex-wrap items-center gap-2")[
                UiDropdown
                    .Key("controlled")
                    .Trigger(_menuOpen ? "Close menu" : "Open menu")
                    .Position(UiPosition.Bottom)
                    .Open(_menuOpen)
                    .OnToggle(open => { _menuOpen = open; })[
                    MenuAction("rename", "Rename", "⌘R"),
                    MenuAction("duplicate", "Duplicate", "⌘D"),
                    MenuAction("delete", "Delete", null, UiTone.Error)
                ],
                UiDropdown.Key("rich").Trigger("View").Icon(UiIconName.Sparkles).Align(UiAlign.End)[
                    UiMenuGroup.Key("sort-group").Heading("Arrange")[
                        UiMenuSub.Key("sort").Heading("Sort by")[
                            UiMenuRadioGroup.Value(_sort)
                                .Options([("name", "Name"), ("date", "Date modified"), ("size", "Size")])
                                .OnChange(sort => { _sort = sort; _lastAction = "sorted by " + sort; })
                        ],
                        UiMenuItem.Key("refresh").Text("Refresh").Kbd("⌘⇧R")
                            .OnClick(() => { _lastAction = "refreshed"; })
                    ],
                    UiMenuSeparator.Key("sep"),
                    UiMenuCheckbox.Key("archived").Value(_showArchived).Text("Show archived")
                        .OnChange(on => { _showArchived = on; _lastAction = on ? "showing archived" : "hiding archived"; }),
                    UiMenuItem.Key("export").Text("Export").Disabled(true)
                ]
            ]),

        Section(
            "Popover — a panel, not a menu",
            "The gap a dropdown leaves. A dropdown IS a menu: its children are rows you pick from, it says "
            + "role=menu and it walks a cursor over them with the arrow keys. A filter panel is none of those, "
            + "and putting one in a menu tells a screen reader it is a list of commands and traps the arrows "
            + "inside it. Same machinery, no menu semantics — a [popover] the browser lifts, dismisses on "
            + "Escape and on a click outside, placed with the same Position and Align everything else uses.",
            Div.Data(Testid("ui-popover"))[
                UiPopover.Trigger("Filters").Icon(UiIconName.Gear).Align(UiAlign.Start)
                    .PanelClass("w-72")[
                    UiHeading.Key("h").Level(3).Size(UiSize.Sm).Class("mb-2")["Narrow the list"],
                    UiCheckboxGroup.Values(_filters).Key("f")
                        .Options([("open", "Open"), ("mine", "Assigned to me"), ("old", "Older than a week")])
                        .Label("Show")
                        .OnChange(v => { _filters = [.. v]; })
                ]
            ]),

        Section(
            "Modal — the popover path (the default)",
            "A real modal <dialog>, opened by an invoker command. The browser gives it the top layer, an "
            + "inert page behind, Escape and focus back on the trigger when it closes, none of it implemented here and none of it "
            + "needing a runtime — this one works with scripting off entirely.",
            Div.Data(Testid("ui-modal-popover"))[
                UiModal
                    .Title("Keyboard shortcuts")
                    .Id("demo-shortcuts")
                    .Trigger("Show shortcuts")[
                    P["Press Escape, or click outside, and the browser closes this. No handler ran."]
                ]
            ]),

        Section(
            "Modal — a flyout",
            "Position Start or End slides it in from that edge at full height — a filter panel, a detail "
            + "sheet. Dismissible(false) keeps a stray click outside from losing what is being edited.",
            Div.Data(Testid("ui-modal-flyout"))[
                UiModal
                    .Title("Filters")
                    .Id("demo-filters")
                    .Trigger("Filters")
                    .Position(UiModalPosition.End)
                    .Dismissible(false)
                    .Footer(UiButton.Tone(UiTone.Primary).Command("close").CommandFor("demo-filters")["Apply"])[
                    P["Only the close button, Escape, or Apply closes this one."]
                ]
            ]),

        Section(
            "Modal — the state-driven path",
            "For when something in C# decides the dialog should appear, which the declarative path "
            + "cannot express: nothing in C# can press a button.",
            Div.Data(Testid("ui-modal"))[
                UiButton
                    .Tone(UiTone.Error)
                    .OnClick(() => { _confirming = true; })["Delete order"],
                _confirming
                    ? UiModal
                        .Title("Delete order")
                        .OnClose(() => { _confirming = false; })
                        .Footer(Div.Class("flex flex-wrap gap-2 sm:justify-end")[
                            UiButton.Key("cancel").Variant(UiVariant.Ghost)
                                .OnClick(() => { _confirming = false; })["Cancel"],
                            UiButton.Key("confirm").Tone(UiTone.Error)
                                .OnClick(() =>
                                {
                                    _confirming = false;
                                    _lastAction = "deleted the order";
                                })["Delete"]
                        ])[
                        P["This cannot be undone."]
                    ]
                    : null
            ]),

        Section(
            "Swap",
            "Two faces, one shown at a time. The state is a bool on this component, not a checkbox in "
            + "the DOM, so something else changing the value corrects the face.",
            Div.Data(Testid("ui-swap")).Class("flex items-center gap-3")[
                UiSwap
                    .AccessibleLabel(_muted ? "Unmute" : "Mute")
                    .On(UiIcon.Name(UiIconName.Close).Class("size-5"))
                    .Off(UiIcon.Name(UiIconName.Check).Class("size-5"))
                    .Animation(UiSwapAnimation.Rotate)
                    .Active(_muted)
                    .OnChange(muted => { _muted = muted; }),
                Span.Class("text-sm text-ui-muted")[_muted ? "Muted" : "Playing"]
            ]),

        Section(
            "Theme controller",
            "It reports a choice; the page applies it. The control cannot write data-theme itself, "
            + "because the scope that carries it is an ancestor — so this page holds the value and puts "
            + "it on the box below, which re-themes just that subtree.",
            Div.Data(Testid("ui-theme-controller"))[
                Div.Class("flex flex-wrap items-center gap-2")[
                    ThemeButton("light", "Light", UiThemeName.Light),
                    ThemeButton("dark", "Dark", UiThemeName.Dark),
                    ThemeButton("retro", "Retro", UiThemeName.Retro)
                ],
                // The applying half, and the reason the control has no way to do this itself: daisyUI
                // matches [data-theme=x] on any ANCESTOR, so whoever owns the value writes it above the
                // things it should repaint.
                Div
                    .Attributes(("data-theme", UiTheme.Value(_theme)))
                    .Data(Testid("ui-theme-scope"))
                    .Class("mt-3 rounded-xl border border-base-300 bg-base-100 p-4 text-base-content")[
                    P.Class("text-sm")[$"This box is painted by the {UiTheme.Value(_theme)} theme."],
                    Div.Class("mt-2 flex gap-2")[
                        UiButton.Key("p").Tone(UiTone.Primary).Size(UiSize.Sm)["Primary"],
                        UiButton.Key("a").Tone(UiTone.Accent).Size(UiSize.Sm)["Accent"]
                    ]
                ]
            ]),

        Section(
            "Floating action button",
            "Opened by the browser on focus-within — daisyUI defines no class to force it, so this one "
            + "is deliberately not state-driven. It is pinned to the corner of the VIEWPORT, which is "
            + "what a FAB is; look bottom-right, then tab to it or click it.",
            // Not repositioned into this section, deliberately. `.fab` is `position: fixed`, and pulling
            // it back into the flow would mean a site utility overriding a kit component class ACROSS
            // TWO <link> stylesheets — where CSS layers do not merge, so which rule wins stops being
            // something either sheet decides. It is also `pointer-events: none` on the container, so a
            // floating FAB intercepts nothing but its own buttons.
            Div.Data(Testid("ui-fab"))[
                UiFab
                    .AccessibleLabel("Compose")
                    .Icon(UiIconName.Plus)[
                    UiButton.Key("photo").Size(UiSize.Sm)["Photo"],
                    UiButton.Key("file").Size(UiSize.Sm)["File"]
                ]
            ]),

        P.Class("mt-6 text-sm text-ui-muted")
            .Data(Testid("ui-actions-log"))[$"Last action: {_lastAction}."]
    ];

    private static Dictionary<string, string?> Testid(string value) => new() { ["testid"] = value };

    private Component ThemeButton(string key, string label, UiThemeName theme) =>
        UiThemeController
            .Key(key)
            .Label(label)
            .Theme(theme)
            .Size(UiSize.Sm)
            .Active(_theme == theme)
            .OnChange(chosen => { _theme = chosen; });

    private Component MenuAction(string key, string label, string? kbd, UiTone? tone = null) =>
        UiMenuItem.Key(key).Text(label).Kbd(kbd).Tone(tone).OnClick(() =>
        {
            _lastAction = label.ToLowerInvariant();
            _menuOpen = false;
        });

    private static Component Section(string heading, string blurb, Component body) =>
        Div.Key(heading).Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            body
        ];
}
