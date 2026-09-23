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
    private Ui.ThemeName _theme = Ui.ThemeName.Light;
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
                Ui.Button.Key("solid").Tone(Ui.Tone.Primary)["Primary"],
                Ui.Button.Key("outline").Tone(Ui.Tone.Error).Variant(Ui.Variant.Outline)["Outline"],
                Ui.Button.Key("soft").Tone(Ui.Tone.Success).Variant(Ui.Variant.Soft)["Soft"],
                Ui.Button.Key("dash").Tone(Ui.Tone.Warning).Variant(Ui.Variant.Dash)["Dash"],
                Ui.Button.Key("ghost").Variant(Ui.Variant.Ghost)["Ghost"],
                Ui.Button.Key("link").Variant(Ui.Variant.Link)["Link"],
                Ui.Button.Key("wide").Wide(true)["Wide"],
                Ui.Button.Key("circle").AccessibleLabel("Close").Circle(true)[Ui.Icon.Name(Ui.IconName.Close)],
                Ui.Button.Key("square").AccessibleLabel("Add").Square(true)[Ui.Icon.Name(Ui.IconName.Plus)],
                Ui.Button.Key("disabled").Disabled(true)["Disabled"]
            ]),

        Section(
            "Button — waiting on its handler",
            "No property to set. A button whose handler is still running after 200 ms shows a spinner at the "
            + "same width, tells a screen reader it is busy, and drops a second press until the first is done. "
            + "Loading(false) opts a stepper out, so its presses queue.",
            Div.Data(Testid("ui-button-loading")).Class("flex flex-wrap items-center gap-3")[
                Ui.Button.Key("slow-save").Tone(Ui.Tone.Primary).OnClick(async () =>
                {
                    await Task.Delay(1500);
                    _saves++;
                })["Save"],
                Ui.Button.Key("stepper").Loading(false).OnClick(async () =>
                {
                    await Task.Delay(400);
                    _steps++;
                })[Ui.Icon.Name(Ui.IconName.Plus), "Step"],
                Span.Data(Testid("ui-button-loading-count")).Class("text-sm text-ui-muted")[
                    $"Saved {_saves} time{(_saves == 1 ? "" : "s")} · stepped {_steps}"
                ]
            ]),

        Section(
            "Button and link — going somewhere",
            "Given a generated route, a button or a link is an <a> the runtime routes inside the app, so the "
            + "page changes without reloading. A plain string stays an ordinary link, for a URL that leaves.",
            Div.Data(Testid("ui-button-route")).Class("flex flex-wrap items-center gap-3")[
                Ui.Button.Key("to-navigation").Tone(Ui.Tone.Primary).Variant(Ui.Variant.Outline)
                    .Href(PageMeta.LinkTo(Routes.UiKitNavigationPage()))["Navigation components"],
                Ui.Link.Key("to-data-display").Href(PageMeta.LinkTo(Routes.UiKitDataDisplayPage()))
                    .Text("Data display components"),
                Ui.Button.Key("to-github").Variant(Ui.Variant.Ghost)
                    .Href("https://github.com/pal-tamas/rask").NewTab(true)["GitHub"]
            ]),

        Section(
            "Dropdown",
            "A popover menu with Flux UI's keyboard: the arrows move a cursor, Home and End jump, a letter "
            + "jumps to the next item starting with it, Right opens a submenu and Left closes it, Enter picks, "
            + "Escape and Tab leave. A pointer crossing diagonally into a submenu keeps it open — the safe "
            + "triangle. Open is nullable: unset leaves it to the reader, true and false hand it to this page.",
            Div.Data(Testid("ui-dropdown")).Class("flex flex-wrap items-center gap-2")[
                Ui.Dropdown
                    .Key("controlled")
                    .Trigger(_menuOpen ? "Close menu" : "Open menu")
                    .Position(Ui.Position.Bottom)
                    .Open(_menuOpen)
                    .OnToggle(open => { _menuOpen = open; })[
                    MenuAction("rename", "Rename", "⌘R"),
                    MenuAction("duplicate", "Duplicate", "⌘D"),
                    MenuAction("delete", "Delete", null, Ui.Tone.Error)
                ],
                Ui.Dropdown.Key("rich").Trigger("View").Icon(Ui.IconName.Sparkles).Align(Ui.Align.End)[
                    Ui.MenuGroup.Key("sort-group").Heading("Arrange")[
                        Ui.MenuSub.Key("sort").Heading("Sort by")[
                            Ui.MenuRadioGroup.Value(_sort)
                                .Options([("name", "Name"), ("date", "Date modified"), ("size", "Size")])
                                .OnChange(sort => { _sort = sort; _lastAction = "sorted by " + sort; })
                        ],
                        Ui.MenuItem.Key("refresh").Text("Refresh").Kbd("⌘⇧R")
                            .OnClick(() => { _lastAction = "refreshed"; })
                    ],
                    Ui.MenuSeparator.Key("sep"),
                    Ui.MenuCheckbox.Key("archived").Value(_showArchived).Text("Show archived")
                        .OnChange(on => { _showArchived = on; _lastAction = on ? "showing archived" : "hiding archived"; }),
                    Ui.MenuItem.Key("export").Text("Export").Disabled(true)
                ]
            ]),

        Section(
            "Context menu",
            "Right-click the card. It is the same menu a dropdown draws — the same rows, the same keyboard — opened "
            + "at the pointer instead of from a button. The runtime opens it, so it appears at once on either host, "
            + "and the ContextMenu key or Shift+F10 opens it at the focused element. Nothing in it should be the "
            + "only way to do something: iOS never fires the event.",
            Div.Data(Testid("ui-context-menu"))[
                Ui.ContextMenu.Target(
                    Div.TabIndex(0)
                        .Class("rounded-box border border-dashed border-base-300 p-6 text-center text-sm text-ui-muted")[
                        "Right-click this card"
                    ])[
                    Ui.MenuItem.Key("open").Text("Open").OnClick(() => { _lastAction = "opened the card"; }),
                    Ui.MenuItem.Key("copy").Text("Copy link").OnClick(() => { _lastAction = "copied the link"; }),
                    Ui.MenuSeparator.Key("sep"),
                    Ui.MenuItem.Key("delete").Text("Delete").Tone(Ui.Tone.Error)
                        .OnClick(() => { _lastAction = "deleted the card"; })
                ]
            ]),

        Section(
            "Command palette",
            "Click the field, or press ⌘K (Ctrl K off a Mac) anywhere on this page. The commands are the rows a "
            + "dropdown takes; typing narrows them, the arrows move the highlight while focus stays in the box, "
            + "and Enter runs the highlighted one — its handler, or its link — and closes the palette. The "
            + "shortcut is a runtime hook that clicks the field, so it opens the same dialog a click does.",
            Div.Data(Testid("ui-command")).Class("max-w-sm")[
                Ui.Command.Label("Search commands").Shortcut("mod+k")[
                    Ui.MenuGroup.Heading("Invoices")[
                        Ui.MenuItem.Text("New invoice").Icon(Ui.IconName.Plus)
                            .OnClick(() => { _lastAction = "started a new invoice"; }),
                        Ui.MenuItem.Text("Export all").Icon(Ui.IconName.Download).Disabled(true)
                    ],
                    Ui.MenuSeparator,
                    Ui.MenuItem.Text("Copy invoice link").Icon(Ui.IconName.Clipboard)
                        .OnClick(() => { _lastAction = "copied the invoice link"; }),
                    Ui.MenuItem.Text("Sign out").Tone(Ui.Tone.Error)
                        .OnClick(() => { _lastAction = "signed out"; })
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
                Ui.Popover.Trigger("Filters").Icon(Ui.IconName.Gear).Align(Ui.Align.Start)
                    .PanelClass("w-72")[
                    Ui.Heading.Key("h").Level(3).Size(Ui.Size.Sm).Class("mb-2")["Narrow the list"],
                    Ui.CheckboxGroup.Values(_filters).Key("f")
                        .Options([("open", "Open"), ("mine", "Assigned to me"), ("old", "Older than a week")])
                        .Label("Show")
                        .OnChange(v => { _filters = [.. v]; })
                ]
            ]),

        Section(
            "Modal — the popover path (the default)",
            "A real modal <dialog>, opened by an invoker command. The browser gives it the top layer, an "
            + "inert page behind, Escape and focus back on the trigger when it closes, none of it implemented here and none of it "
            + "needing a runtime — this one works with scripting off entirely. OnClose only hears that it closed.",
            Div.Data(Testid("ui-modal-popover"))[
                Ui.Modal
                    .Title("Keyboard shortcuts")
                    .Id("demo-shortcuts")
                    .Trigger("Show shortcuts")
                    .OnClose(() => { _lastAction = "closed the shortcuts"; })[
                    P["Press Escape, or click outside, and the browser closes this."],
                    // A toggle INSIDE the dialog is the dialog's descendant's event, not the dialog's own.
                    Details.Data(Testid("ui-modal-popover-more"))[
                        Summary["More shortcuts"],
                        P["⌘K opens the command palette."]
                    ]
                ]
            ]),

        Section(
            "Modal — a flyout",
            "Position Start or End slides it in from that edge at full height — a filter panel, a detail "
            + "sheet. Dismissible(false) keeps a stray click outside from losing what is being edited.",
            Div.Data(Testid("ui-modal-flyout"))[
                Ui.Modal
                    .Title("Filters")
                    .Id("demo-filters")
                    .Trigger("Filters")
                    .Position(Ui.ModalPosition.End)
                    .Dismissible(false)
                    .Footer(Ui.Button.Tone(Ui.Tone.Primary).Command("close").CommandFor("demo-filters")["Apply"])[
                    P["Only the close button, Escape, or Apply closes this one."]
                ]
            ]),

        Section(
            "Modal — the state-driven path",
            "For when something in C# decides the dialog should appear, which the declarative path "
            + "cannot express: nothing in C# can press a button. OnCancel hears a dismissal — Escape or a "
            + "click outside — apart from a close, so backing out is logged differently from Cancel.",
            Div.Data(Testid("ui-modal"))[
                Ui.Button
                    .Tone(Ui.Tone.Error)
                    .OnClick(() => { _confirming = true; })["Delete order"],
                _confirming
                    ? Ui.Modal
                        .Title("Delete order")
                        .OnCancel(() => { _lastAction = "dismissed the dialog"; })
                        .OnClose(() => { _confirming = false; })
                        .Footer(Div.Class("flex flex-wrap gap-2 sm:justify-end")[
                            Ui.Button.Key("cancel").Variant(Ui.Variant.Ghost)
                                .OnClick(() => { _confirming = false; })["Cancel"],
                            Ui.Button.Key("confirm").Tone(Ui.Tone.Error)
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
                Ui.Swap
                    .AccessibleLabel(_muted ? "Unmute" : "Mute")
                    .On(Ui.Icon.Name(Ui.IconName.Close).Class("size-5"))
                    .Off(Ui.Icon.Name(Ui.IconName.Check).Class("size-5"))
                    .Animation(Ui.SwapAnimation.Rotate)
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
                    ThemeButton("light", "Light", Ui.ThemeName.Light),
                    ThemeButton("dark", "Dark", Ui.ThemeName.Dark),
                    ThemeButton("retro", "Retro", Ui.ThemeName.Retro)
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
                        Ui.Button.Key("p").Tone(Ui.Tone.Primary).Size(Ui.Size.Sm)["Primary"],
                        Ui.Button.Key("a").Tone(Ui.Tone.Accent).Size(Ui.Size.Sm)["Accent"]
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
                Ui.Fab
                    .AccessibleLabel("Compose")
                    .Icon(Ui.IconName.Plus)[
                    Ui.Button.Key("photo").Size(Ui.Size.Sm)["Photo"],
                    Ui.Button.Key("file").Size(Ui.Size.Sm)["File"]
                ]
            ]),

        P.Class("mt-6 text-sm text-ui-muted")
            .Data(Testid("ui-actions-log"))[$"Last action: {_lastAction}."]
    ];

    private static Dictionary<string, string?> Testid(string value) => new() { ["testid"] = value };

    private Component ThemeButton(string key, string label, Ui.ThemeName theme) =>
        Ui.ThemeController
            .Key(key)
            .Label(label)
            .Theme(theme)
            .Size(Ui.Size.Sm)
            .Active(_theme == theme)
            .OnChange(chosen => { _theme = chosen; });

    private Component MenuAction(string key, string label, string? kbd, Ui.Tone? tone = null) =>
        Ui.MenuItem.Key(key).Text(label).Kbd(kbd).Tone(tone).OnClick(() =>
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
