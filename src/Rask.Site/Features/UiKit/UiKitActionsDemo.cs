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

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        Section(
            "Button",
            "Colour, fill and size are three independent axes and compose, so an outlined error button "
            + "needs no member of its own.",
            Div.Data(Testid("ui-button")).Class("flex flex-wrap items-center gap-2")[
                UiButton.Key("solid").Label("Primary").Tone(UiTone.Primary),
                UiButton.Key("outline").Label("Outline").Tone(UiTone.Error).Variant(UiVariant.Outline),
                UiButton.Key("soft").Label("Soft").Tone(UiTone.Success).Variant(UiVariant.Soft),
                UiButton.Key("dash").Label("Dash").Tone(UiTone.Warning).Variant(UiVariant.Dash),
                UiButton.Key("ghost").Label("Ghost").Variant(UiVariant.Ghost),
                UiButton.Key("link").Label("Link").Variant(UiVariant.Link),
                UiButton.Key("wide").Label("Wide").Wide(true),
                UiButton.Key("circle").Label("Close").Circle(true).Icon(UiIconName.Close),
                UiButton.Key("square").Label("Add").Square(true).Icon(UiIconName.Plus),
                UiButton.Key("disabled").Label("Disabled").Disabled(true)
            ]),

        Section(
            "Dropdown",
            "Open is nullable, and the three settings mean three different things: unset lets the "
            + "browser open it on focus, true and false hand the decision to this page.",
            Div.Data(Testid("ui-dropdown")).Class("flex flex-wrap items-center gap-2")[
                UiDropdown
                    .Key("controlled")
                    .Trigger(_menuOpen ? "Close menu" : "Open menu")
                    .Placement(UiPlacement.Bottom)
                    .Open(_menuOpen)
                    .OnToggle(open => { _menuOpen = open; })[
                    MenuAction("rename", "Rename"),
                    MenuAction("duplicate", "Duplicate"),
                    MenuAction("delete", "Delete")
                ],
                UiDropdown.Key("uncontrolled").Trigger("Uncontrolled").Placement(UiPlacement.End)[
                    MenuAction("first", "Opens on focus"),
                    MenuAction("second", "Closes when focus leaves")
                ]
            ]),

        Section(
            "Modal — the popover path (the default)",
            "A real <dialog> with the popover attribute. The browser gives it the top layer, Escape, "
            + "light-dismiss and a native ::backdrop, none of it implemented here and none of it "
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
            "Modal — the state-driven path",
            "For when something in C# decides the dialog should appear, which the declarative path "
            + "cannot express: nothing in C# can press a button.",
            Div.Data(Testid("ui-modal"))[
                UiButton
                    .Label("Delete order")
                    .Tone(UiTone.Error)
                    .OnClick(() => { _confirming = true; }),
                _confirming
                    ? UiModal
                        .Title("Delete order")
                        .Close(() => { _confirming = false; })
                        .Footer(Div.Class("flex flex-wrap gap-2 sm:justify-end")[
                            UiButton.Key("cancel").Label("Cancel").Variant(UiVariant.Ghost)
                                .OnClick(() => { _confirming = false; }),
                            UiButton.Key("confirm").Label("Delete").Tone(UiTone.Error)
                                .OnClick(() =>
                                {
                                    _confirming = false;
                                    _lastAction = "deleted the order";
                                })
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
                        UiButton.Key("p").Label("Primary").Tone(UiTone.Primary).Size(UiSize.Sm),
                        UiButton.Key("a").Label("Accent").Tone(UiTone.Accent).Size(UiSize.Sm)
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
                    UiButton.Key("photo").Label("Photo").Size(UiSize.Sm),
                    UiButton.Key("file").Label("File").Size(UiSize.Sm)
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

    private Component MenuAction(string key, string label) =>
        Li.Key(key)[
            Button
                .Type("button")
                .Class("w-full text-left")
                .OnClick(() =>
                {
                    _lastAction = label.ToLowerInvariant();
                    _menuOpen = false;
                })[label]
        ];

    private static Component Section(string heading, string blurb, Component body) =>
        Div.Key(heading).Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            body
        ];
}
