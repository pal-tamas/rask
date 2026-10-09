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
    private string _activity = "latest";
    private bool _draft = true;
    private bool _published = true;
    private bool _archived;
    private bool _read = true;
    private bool _write = true;
    private bool _delete;
    private List<string> _filters = ["open"];

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

    // Flux UI's dropdown page, example for example, each wired to this component's state.
    private Component DropdownSection() =>
        Section(
            "Dropdown and menu",
            "A trigger and the menu it opens — Flux UI's dropdown, row for row. It opens with focus on the menu; "
            + "the arrows walk the rows and stop at the ends, a letter jumps to the row that starts with it, Right "
            + "or Enter opens a submenu and Left closes it, Enter or Space picks, Escape and Tab leave. A pick "
            + "closes the menu unless the menu, or the row, says KeepOpen. The page behind does not scroll, and a "
            + "click outside only closes it.",
            Div.Data(Testid("ui-dropdown")).Class("flex flex-wrap items-center gap-2")[
                Ui.Dropdown.Key("options")[Trigger("Options"), PostMenu("list", "the list")],
                Ui.Dropdown.Key("account").Bottom.End[Trigger("Olivia Martin"), AccountLinks()],
                Ui.Dropdown.Key("above").Top.Start[Trigger("Above"), Places("above")],
                Ui.Dropdown.Key("nudged").Offset(-15).Gap(2)[Trigger("Offset and gap"), Places("nudged")],
                Ui.Dropdown.Key("shortcuts")[Trigger("Shortcuts"), ShortcutMenu()],
                Ui.Dropdown.Key("permissions")[Trigger("Permissions"), PermissionMenu()],
                Ui.Dropdown.Key("activity")[Trigger("Sort by"), ActivityMenu()],
                Ui.Dropdown.Key("groups")[Trigger("Groups"), GroupedMenu()],
                Ui.Dropdown.Key("headings")[Trigger("Headings"), HeadedMenu()],
                // KeepOpen on the menu, then on the rows alone: there "Clear" still closes it.
                Ui.Dropdown.Key("keeps")[Trigger("Keep open"), Ui.Menu.KeepOpen()[FilterRows("keeps", keepOpen: false)]],
                Ui.Dropdown.Key("keeps-rows")[Trigger("Filters"), ClearableFilterMenu()],
                // Open is nullable: unset leaves it to the reader, true and false hand it to this page.
                Ui.Dropdown.Key("controlled").Open(_menuOpen).OnToggle(open => { _menuOpen = open; })[
                    Trigger(_menuOpen ? "Close menu" : "Open menu"),
                    Ui.Menu[
                        Ui.MenuItem.Key("rename").Kbd("⌘R").OnClick(() => Did("rename", close: true))["Rename"],
                        Ui.MenuItem.Key("archive").OnClick(() => Did("archive", close: true))["Archive"]
                    ]
                ]
            ]);

    // The lead example of Flux's page, and the menu its context page opens: an item, two submenus, a danger item.
    // It is written twice on this page, so every key carries the id of the menu it is in: a key names ONE
    // component among all those this page writes, and two rows sharing one would share their props.
    private Component PostMenu(string id, string what) =>
        Ui.Menu[
            Ui.MenuItem.Key(id + "-new").Icon(Ui.IconName.Plus).OnClick(() => Did("new post in " + what))["New post"],
            Ui.MenuSeparator.Key(id + "-s1"),
            Ui.MenuSubmenu.Key(id + "-sort").Heading("Sort by")[
                Ui.MenuRadioGroup.Value(_sort).OnChange(sort => { _sort = sort; Did("sorted by " + sort); })[
                    Ui.MenuRadio.Key(id + "-name").Value("name")["Name"],
                    Ui.MenuRadio.Key(id + "-date").Value("date")["Date"],
                    Ui.MenuRadio.Key(id + "-popularity").Value("popularity")["Popularity"]
                ]
            ],
            Ui.MenuSubmenu.Key(id + "-filter").Heading("Filter")[FilterRows(id, keepOpen: false)],
            Ui.MenuSeparator.Key(id + "-s2"),
            Ui.MenuItem.Key(id + "-delete").Danger.Icon(Ui.IconName.Trash).OnClick(() => Did("deleted from " + what))["Delete"]
        ];

    private Component[] FilterRows(string id, bool keepOpen) =>
    [
        Ui.MenuCheckbox.Value(_draft).Key(id + "-draft").KeepOpen(keepOpen).OnChange(on => { _draft = on; })["Draft"],
        Ui.MenuCheckbox.Value(_published).Key(id + "-published").KeepOpen(keepOpen).OnChange(on => { _published = on; })["Published"],
        Ui.MenuCheckbox.Value(_archived).Key(id + "-archived").KeepOpen(keepOpen).OnChange(on => { _archived = on; })["Archived"]
    ];

    private Component ClearableFilterMenu()
    {
        Component[] rows =
        [
            .. FilterRows("rows", keepOpen: true),
            Ui.MenuSeparator.Key("rows-s1"),
            Ui.MenuItem.Key("rows-clear").Danger.OnClick(() =>
            {
                _draft = _published = _archived = false;
                Did("cleared the filters");
            })["Clear"]
        ];

        return Ui.Menu[rows];
    }

    // A navigation menu: links, with no menu roles and no cursor.
    private static Component AccountLinks() =>
        Ui.Navmenu[
            Ui.NavmenuItem.Key("link-account").Href("#account").Icon(Ui.IconName.User)["Account"],
            Ui.NavmenuItem.Key("link-profile").Href("#profile").Icon(Ui.IconName.BuildingStorefront)["Profile"],
            Ui.NavmenuItem.Key("link-billing").Href("#billing").Icon(Ui.IconName.CreditCard)["Billing"],
            Ui.NavmenuItem.Key("link-logout").Href("#logout").Icon(Ui.IconName.ArrowRightStartOnRectangle)["Logout"],
            Ui.NavmenuItem.Key("link-delete").Href("#delete").Icon(Ui.IconName.Trash).Danger["Delete"]
        ];

    // The navigation menu Flux's two placement examples open.
    private static Component Places(string id) =>
        Ui.Navmenu[
            Ui.NavmenuItem.Key(id + "-account").Href("#account")["Account"],
            Ui.NavmenuItem.Key(id + "-profile").Href("#profile")["Profile"],
            Ui.NavmenuItem.Key(id + "-billing").Href("#billing")["Billing"],
            Ui.NavmenuItem.Key(id + "-logout").Href("#logout")["Logout"]
        ];

    private Component ShortcutMenu() =>
        Ui.Menu[
            Ui.MenuItem.Key("shortcut-save").Icon(Ui.IconName.PencilSquare).Kbd("⌘S").OnClick(() => Did("save"))["Save"],
            Ui.MenuItem.Key("shortcut-duplicate").Icon(Ui.IconName.DocumentDuplicate).Kbd("⌘D").OnClick(() => Did("duplicate"))["Duplicate"],
            Ui.MenuItem.Key("shortcut-export").Icon(Ui.IconName.ArrowDownTray).Suffix("PDF").Disabled()["Export"],
            Ui.MenuItem.Key("shortcut-delete").Icon(Ui.IconName.Trash).Danger.Kbd("⌘⌫").OnClick(() => Did("delete"))["Delete"]
        ];

    private Component PermissionMenu() =>
        Ui.Menu[
            Ui.MenuCheckbox.Value(_read).Key("may-read").OnChange(on => { _read = on; Did(on ? "may read" : "may not read"); })["Read"],
            Ui.MenuCheckbox.Value(_write).Key("may-write").OnChange(on => { _write = on; })["Write"],
            Ui.MenuCheckbox.Value(_delete).Key("may-delete").OnChange(on => { _delete = on; Did(on ? "may delete" : "may not delete"); })["Delete"]
        ];

    private Component ActivityMenu() =>
        Ui.Menu[
            Ui.MenuRadioGroup.Value(_activity).OnChange(by => { _activity = by; Did("ordered by " + by); })[
                Ui.MenuRadio.Key("by-latest").Value("latest")["Latest activity"],
                Ui.MenuRadio.Key("by-created").Value("created")["Date created"],
                Ui.MenuRadio.Key("by-popular").Value("popular")["Most popular"]
            ]
        ];

    private Component GroupedMenu() =>
        Ui.Menu[
            Ui.MenuItem.Key("grouped-view").OnClick(() => Did("view"))["View"],
            Ui.MenuItem.Key("grouped-transfer").OnClick(() => Did("transfer"))["Transfer"],
            Ui.MenuSeparator.Key("grouped-s1"),
            Ui.MenuItem.Key("grouped-publish").OnClick(() => Did("publish"))["Publish"],
            Ui.MenuItem.Key("grouped-share").OnClick(() => Did("share"))["Share"],
            Ui.MenuSeparator.Key("grouped-s2"),
            Ui.MenuItem.Key("grouped-delete").Danger.OnClick(() => Did("delete"))["Delete"]
        ];

    private static Component HeadedMenu() =>
        Ui.Menu[
            Ui.MenuGroup.Key("headed-account").Heading("Account")[
                Ui.MenuItem.Key("headed-profile")["Profile"],
                Ui.MenuItem.Key("headed-permissions")["Permissions"]
            ],
            Ui.MenuGroup.Key("headed-billing").Heading("Billing")[
                Ui.MenuItem.Key("headed-transactions")["Transactions"],
                Ui.MenuItem.Key("headed-payouts")["Payouts"],
                Ui.MenuItem.Key("headed-refunds")["Refunds"]
            ],
            Ui.MenuItem.Key("headed-logout")["Logout"]
        ];

    // Any button is a trigger: the dropdown adds what makes it one.
    private static Component Trigger(string label) =>
        Ui.Button.IconTrailing(Ui.IconName.ChevronDown)[label];

    private void Did(string action, bool close = false)
    {
        _lastAction = action;
        if (close)
        {
            _menuOpen = false;
        }
    }

    private Component ContextMenuSection() =>
        Section(
            "Context menu",
            "Right-click the card. It is the same menu a dropdown opens — the same rows, the same keyboard — opened "
            + "at the pointer instead of from a button. The runtime opens it, so it appears at once on either host, "
            + "and the ContextMenu key or Shift+F10 opens it at the focused element. Nothing in it should be the "
            + "only way to do something: iOS never fires the event.",
            Div.Data(Testid("ui-context-menu"))[
                Ui.Context[
                    Div.TabIndex(0)
                        .Class("inline-block rounded-box border-2 border-dashed border-base-300 px-16 py-6 text-sm text-ui-muted")[
                        "Right click"
                    ],
                    PostMenu("card", "the card")
                ]
            ]);

    private Component CommandPaletteSection() =>
        Section(
            "Command palette",
            "Click the field, or press ⌘K (Ctrl K off a Mac) anywhere on this page. The commands are written as the "
            + "rows a menu takes; typing narrows them, the arrows move the highlight while focus stays in the box, "
            + "and Enter runs the highlighted one — its handler, or its link — and closes the palette. The "
            + "shortcut is a runtime hook that clicks the field, so it opens the same dialog a click does.",
            Div.Data(Testid("ui-command")).Class("max-w-sm")[
                Ui.Command.Label("Search commands").Shortcut("mod+k")[
                    Ui.MenuGroup.Heading("Invoices")[
                        Ui.MenuItem.Icon(Ui.IconName.Plus).OnClick(() => { _lastAction = "started a new invoice"; })["New invoice"],
                        Ui.MenuItem.Icon(Ui.IconName.ArrowDownTray).Disabled()["Export all"]
                    ],
                    Ui.MenuSeparator,
                    Ui.MenuItem.Icon(Ui.IconName.ClipboardDocumentCheck)
                        .OnClick(() => { _lastAction = "copied the invoice link"; })["Copy invoice link"],
                    Ui.MenuItem.Danger.OnClick(() => { _lastAction = "signed out"; })["Sign out"]
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
                    Ui.CheckboxGroup.Values(_filters).Key("f").Id("popover-filters").Label("Show")
                        .OnChange(v => { _filters = [.. v]; })[
                        Ui.Checkbox.Value("open").Label("Open"),
                        Ui.Checkbox.Value("mine").Label("Assigned to me"),
                        Ui.Checkbox.Value("old").Label("Older than a week")
                    ]
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
                // The one modal on this page without a Key: it keeps its instance by being the only unkeyed one.
                Ui.Modal
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


    private static Component Section(string heading, string blurb, Component body) =>
        Div.Key(heading).Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            body
        ];
}
