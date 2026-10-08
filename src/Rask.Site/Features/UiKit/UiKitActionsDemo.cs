namespace Rask.Site.Features.UiKit;

/// <summary>
///     The kit's actions — Flux's button first, in <see cref="UiKitButtonDemo" /> — driven by this component's own state.
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
    private string _lastAction = "nothing yet";
    private string _sort = "name";
    private List<string> _filters = ["open"];
    private bool _showArchived;

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        UiKitButtonDemo,
        DropdownSection(),
        ContextMenuSection(),
        CommandPaletteSection(),
        PopoverSection(),
        ModalSection(),
        ConfirmationModalSection(),
        FlyoutModalSection(),
        FloatingFlyoutModalSection(),
        ModalOptionsSection(),
        StateDrivenModalSection(),

        SwapSection(),
        FloatingActionButtonSection(),

        P.Class("mt-6 text-sm text-ui-muted")
            .Data(Testid("ui-actions-log"))[$"Last action: {_lastAction}."]
    ];

    private Component DropdownSection() =>
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
                    Ui.MenuGroup.Key("sort-group").Title("Arrange")[
                        Ui.MenuSub.Key("sort").Title("Sort by")[
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
                    Ui.MenuItem.Key("export").Text("Export").Disabled()
                ]
            ]);

    private Component ContextMenuSection() =>
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
                    Ui.MenuItem.Key("delete").Text("Delete").Error
                        .OnClick(() => { _lastAction = "deleted the card"; })
                ]
            ]);

    private Component CommandPaletteSection() =>
        Section(
            "Command palette",
            "Click the field, or press ⌘K (Ctrl K off a Mac) anywhere on this page. The commands are the rows a "
            + "dropdown takes; typing narrows them, the arrows move the highlight while focus stays in the box, "
            + "and Enter runs the highlighted one — its handler, or its link — and closes the palette. The "
            + "shortcut is a runtime hook that clicks the field, so it opens the same dialog a click does.",
            Div.Data(Testid("ui-command")).Class("max-w-sm")[
                Ui.Command.Label("Search commands").Shortcut("mod+k")[
                    Ui.MenuGroup.Title("Invoices")[
                        Ui.MenuItem.Text("New invoice").Icon(Ui.IconName.Plus)
                            .OnClick(() => { _lastAction = "started a new invoice"; }),
                        Ui.MenuItem.Text("Export all").Icon(Ui.IconName.ArrowDownTray).Disabled()
                    ],
                    Ui.MenuSeparator,
                    Ui.MenuItem.Text("Copy invoice link").Icon(Ui.IconName.ClipboardDocumentCheck)
                        .OnClick(() => { _lastAction = "copied the invoice link"; }),
                    Ui.MenuItem.Text("Sign out").Error
                        .OnClick(() => { _lastAction = "signed out"; })
                ]
            ]);

    private Component PopoverSection() =>
        Section(
            "Popover — a panel, not a menu",
            "The gap a dropdown leaves. A dropdown IS a menu: its children are rows you pick from, it says "
            + "role=menu and it walks a cursor over them with the arrow keys. A filter panel is none of those, "
            + "and putting one in a menu tells a screen reader it is a list of commands and traps the arrows "
            + "inside it. Same machinery, no menu semantics — a [popover] the browser lifts, dismisses on "
            + "Escape and on a click outside, placed with the same Position and Align everything else uses.",
            Div.Data(Testid("ui-popover"))[
                Ui.Popover.Trigger("Filters").Icon(Ui.IconName.Cog6Tooth).Align(Ui.Align.Start)
                    .PanelClass("w-72")[
                    Ui.Heading.Key("h").Level(3).Class("mb-2")["Narrow the list"],
                    Ui.CheckboxGroup.Values(_filters).Key("f")
                        .Options([("open", "Open"), ("mine", "Assigned to me"), ("old", "Older than a week")])
                        .Label("Show")
                        .OnChange(v => { _filters = [.. v]; })
                ]
            ]);

    // Flux's first example: a named modal and the trigger that names it.
    private Component ModalSection() =>
        Section(
            "Modal",
            "A real <dialog>, opened by its trigger with no handler. The button is an invoker, so the browser "
            + "gives the dialog the top layer, an inert page behind it, Escape, a click outside and focus back "
            + "on the trigger — on a prerendered page too. OnClose only hears that it closed.",
            Div.Data(Testid("ui-modal"))[
                Ui.ModalTrigger.Key("edit").Name("edit-profile")[Ui.Button["Edit profile"]],
                Ui.Modal
                    .Key("m-edit")
                    .Name("edit-profile")
                    .Class("md:w-96")
                    .OnClose(() => { _lastAction = "closed the profile"; })[
                    Div.Class("space-y-6")[
                        ProfileHeading(),
                        Ui.Input.Of<string>().Key("edit-name").Label("Name").Placeholder("Your name"),
                        Ui.Input.Of<string>().Key("edit-born").Label("Date of birth").Type(InputType.Date),
                        // A toggle INSIDE the dialog is its descendant's event, not the dialog's own.
                        Details.Data(Testid("ui-modal-more"))[
                            Summary["More"],
                            P["Opening this does not close the dialog."]
                        ],
                        Div.Class("flex")[Ui.Spacer, Ui.Button.Primary["Save changes"]]
                    ]
                ]
            ]);

    private Component ConfirmationModalSection() =>
        Section(
            "Modal — confirmation",
            "Ask before a dangerous action. Ui.ModalClose makes the button inside it close the modal it is in — "
            + "a close, not a dismissal — and a handler on that button still runs.",
            Div.Data(Testid("ui-modal-confirm"))[
                Ui.ModalTrigger.Key("delete").Name("delete-profile")[Ui.Button.Danger["Delete"]],
                Ui.Modal.Key("m-delete").Name("delete-profile").Class("min-w-[22rem]")[
                    Div.Class("space-y-6")[
                        Div[
                            Ui.Heading.Level(2).Lg["Delete project?"],
                            Ui.Text.Class("mt-2")["You're about to delete this project.", Br, "This action cannot be reversed."]
                        ],
                        Div.Class("flex gap-2")[
                            Ui.Spacer,
                            Ui.ModalClose.Key("cancel")[Ui.Button.Ghost["Cancel"]],
                            Ui.ModalClose.Key("delete")[
                                Ui.Button.Danger.OnClick(() => { _lastAction = "deleted the project"; })["Delete project"]
                            ]
                        ]
                    ]
                ]
            ]);

    private static Component FlyoutModalSection() =>
        Section(
            "Modal — flyout",
            "Flyout anchors it to an edge at full height, for longer forms: the right by default, Left or "
            + "Bottom when asked.",
            Div.Data(Testid("ui-modal-flyout"))[
                Div.Class("flex flex-wrap gap-2")[
                    Ui.ModalTrigger.Key("right").Name("edit-profile-flyout")[Ui.Button["Edit profile"]],
                    Ui.ModalTrigger.Key("left").Name("flyout-left")[Ui.Button["From the left"]],
                    Ui.ModalTrigger.Key("bottom").Name("flyout-bottom")[Ui.Button["From the bottom"]]
                ],
                Ui.Modal.Key("m-right").Name("edit-profile-flyout").Flyout().Class("md:w-lg")[
                    Div.Class("space-y-6")[
                        ProfileHeading(),
                        Ui.Input.Of<string>().Key("flyout-name").Label("Name").Placeholder("Your name"),
                        Ui.Input.Of<string>().Key("flyout-born").Label("Date of birth").Type(InputType.Date),
                        Div.Class("flex")[Ui.Spacer, Ui.Button.Primary["Save changes"]]
                    ]
                ],
                Ui.Modal.Key("m-left").Name("flyout-left").Flyout().Left[
                    Div.Class("space-y-6")[Ui.Heading.Level(2).Lg["Navigation"], Ui.Text["A flyout from the left edge."]]
                ],
                Ui.Modal.Key("m-bottom").Name("flyout-bottom").Flyout().Bottom[
                    Div.Class("space-y-6")[Ui.Heading.Level(2).Lg["Share"], Ui.Text["A flyout from the bottom edge."]]
                ]
            ]);

    private static Component FloatingFlyoutModalSection() =>
        Section(
            "Modal — floating flyout",
            "The Floating variant stands the flyout off the edges of the viewport, rounded and shadowed like a panel.",
            Div.Data(Testid("ui-modal-floating"))[
                Ui.ModalTrigger.Key("floating").Name("edit-profile-floating")[Ui.Button["Edit profile"]],
                Ui.Modal.Key("m-floating").Name("edit-profile-floating").Flyout().Floating.Class("md:w-lg")[
                    Div.Class("space-y-6")[
                        ProfileHeading(),
                        Ui.Input.Of<string>().Key("floating-name").Label("Name").Placeholder("Your name"),
                        Ui.Input.Of<string>().Key("floating-born").Label("Date of birth").Type(InputType.Date),
                        Div.Class("flex items-center justify-end gap-2")[
                            Ui.ModalClose.Key("floating-cancel")[Ui.Button["Cancel"]],
                            Ui.Button.Primary["Save changes"]
                        ]
                    ]
                ]
            ]);

    private static Component ModalOptionsSection() =>
        Section(
            "Modal — what closes it, and what scrolls",
            "Dismissible(false) keeps a stray click outside from losing what is being edited; Escapable(false) "
            + "and Closable(false) take Escape and the corner button away. Scroll Body lets a long modal run "
            + "past the bottom of the screen, so it is plain there is more.",
            Div.Data(Testid("ui-modal-options"))[
                Div.Class("flex flex-wrap gap-2")[
                    Ui.ModalTrigger.Key("filters").Name("demo-filters")[Ui.Button["Filters"]],
                    Ui.ModalTrigger.Key("terms").Name("demo-terms")[Ui.Button["Terms"]],
                    Ui.ModalTrigger.Key("session").Name("demo-session")[Ui.Button["Session"]]
                ],
                Ui.Modal.Key("m-filters").Name("demo-filters").Flyout().Dismissible(false)[
                    Div.Class("space-y-6")[
                        Ui.Heading.Level(2).Lg["Filters"],
                        Ui.Text["Only the close button, Escape, or Apply closes this one."],
                        Div.Class("flex")[Ui.Spacer, Ui.ModalClose.Key("apply")[Ui.Button.Primary["Apply"]]]
                    ]
                ],
                Ui.Modal.Key("m-session").Name("demo-session").Escapable(false)[
                    Div.Class("space-y-6")[
                        Ui.Heading.Level(2).Lg["Session expiring"],
                        Ui.Text["Escape does not close this one; a click outside, the corner button or Stay does."],
                        Div.Class("flex")[Ui.Spacer, Ui.ModalClose.Key("stay")[Ui.Button.Primary["Stay signed in"]]]
                    ]
                ],
                Ui.Modal.Key("m-terms").Name("demo-terms").Scroll(Ui.ModalScroll.Body).Class("md:w-lg")[
                    Div.Class("space-y-6")[
                        Ui.Heading.Level(2).Lg["Terms of service"],
                        Enumerable.Range(1, 24)
                            .Select(n => Ui.Text.Key(n)[$"{n}. The whole layer scrolls, not a box inside it."])
                            .ToArray(),

                        Div.Class("flex")[Ui.Spacer, Ui.ModalClose.Key("accept")[Ui.Button.Primary["Accept"]]]
                    ]
                ]
            ]);

    private Component StateDrivenModalSection() =>
        Section(
            "Modal — state-driven",
            "For when something in C# decides the dialog should appear, which a trigger cannot express: "
            + "nothing in C# can press a button. The page owns the state — Open is Rask's wire:model — and it "
            + "is the same modal: top layer, backdrop, Escape, a click outside. OnCancel hears a dismissal "
            + "apart from a close.",
            Div.Data(Testid("ui-modal-state"))[
                Ui.Button.Danger.OnClick(() => { _confirming = true; })["Delete order"],
                // Keyed, as every modal on this page is: once one child of a type carries a Key its parent stops
                // reusing that type by position, and an unkeyed one would be a new instance on every render.
                Ui.Modal
                    .Key("m-state")
                    .Open(_confirming)
                    .OnCancel(() => { _lastAction = "dismissed the dialog"; })
                    .OnClose(() => { _confirming = false; })[
                    Div.Class("space-y-6")[
                        Div[
                            Ui.Heading.Level(2).Lg["Delete order"],
                            Ui.Text.Class("mt-2")["This cannot be undone."]
                        ],
                        Div.Class("flex gap-2")[
                            Ui.Spacer,
                            Ui.ModalClose.Key("state-cancel")[Ui.Button.Key("cancel").Ghost["Cancel"]],
                            Ui.Button.Key("confirm").Danger
                                .OnClick(() =>
                                {
                                    _confirming = false;
                                    _lastAction = "deleted the order";
                                })["Delete"]
                        ]
                    ]
                ]
            ]);

    private static Component ProfileHeading() =>
        Div[
            Ui.Heading.Level(2).Lg["Update profile"],
            Ui.Text.Class("mt-2")["Make changes to your personal details."]
        ];

    private Component SwapSection() =>
        Section(
            "Swap",
            "Two faces, one shown at a time. The state is a bool on this component, not a checkbox in "
            + "the DOM, so something else changing the value corrects the face.",
            Div.Data(Testid("ui-swap")).Class("flex items-center gap-3")[
                Ui.Swap
                    .AccessibleLabel(_muted ? "Unmute" : "Mute")
                    .On(Ui.Icon.Name(Ui.IconName.XMark).Class("size-5"))
                    .Off(Ui.Icon.Name(Ui.IconName.Check).Class("size-5"))
                    .Animation(Ui.SwapAnimation.Rotate)
                    .Active(_muted)
                    .OnChange(muted => { _muted = muted; }),
                Span.Class("text-sm text-ui-muted")[_muted ? "Muted" : "Playing"]
            ]);

    private static Component FloatingActionButtonSection() =>
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
                    Ui.Button.Sm.Key("photo")["Photo"],
                    Ui.Button.Sm.Key("file")["File"]
                ]
            ]);

    private static AttrBag Testid(string value) => new("testid", value);

    private UiMenuItem MenuAction(string key, string label, string? kbd, Ui.Tone? tone = null) =>
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
