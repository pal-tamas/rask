using System.Globalization;

namespace Rask;

/// <summary>
/// A command palette: a search field that opens a dialog of commands, narrowed as the reader types.
/// </summary>
/// <remarks>
/// <para>
/// Flux UI's <c>command</c>, opened the way such palettes are — by clicking the field, or from anywhere on the page
/// with <see cref="Shortcut" />. The children are ordinary <see cref="UiMenuItem" />s, so a command has the
/// <c>Icon</c>, <c>Kbd</c>, <c>Href</c>, <c>OnClick</c>, <c>Tone</c> and <c>Disabled</c> it has in a dropdown, and
/// <see cref="UiMenuGroup" /> headings and <see cref="UiMenuSeparator" />s arrange them.
/// </para>
/// <para>
/// The dialog is the platform's: a modal <c>&lt;dialog&gt;</c> opened by the invoker command <c>show-modal</c>, with
/// <c>popovertarget</c> beside it for a browser without invokers — so the top layer, the inert page behind it,
/// Escape and focus back on the field come with it. Inside, the search box is a <c>combobox</c> over a
/// <c>listbox</c>, and the commands are its <c>option</c>s: focus stays in the box while ArrowUp and ArrowDown move
/// the highlighted command (<c>aria-activedescendant</c>), and Enter presses it — its handler runs or its link is
/// followed — and closes the palette.
/// </para>
/// <para>
/// Typing narrows the list in the browser, case- and accent-insensitively, and the arrows move the highlight
/// there too (<c>data-rask-command</c>): opening the palette, typing into it, walking it and closing it send the
/// page nothing, as Flux's does not. A command that does not match is <c>hidden</c>, so the keyboard never lands
/// on one that is out of sight; separators are hidden while there is a query, since what they separated has been
/// filtered. Pressing a command is the page's own action.
/// </para>
/// </remarks>
public sealed partial class UiCommand : Component
{
    private static int _instances;

    private readonly int _instance = Interlocked.Increment(ref _instances);

    /// <summary>
    ///     What the palette is for — "Search commands". The words on the field, the dialog's name, and the search
    ///     box's name.
    /// </summary>
    public required string Label { get; set; }

    /// <summary>What the search box says while it is empty. <see cref="Label" /> unless this says otherwise.</summary>
    public string? Placeholder { get; set; }

    /// <summary>
    ///     A shortcut that opens the palette from anywhere on the page — <c>"mod+k"</c>, where <c>mod</c> is ⌘ on a
    ///     Mac and Ctrl everywhere else. Modifiers are <c>mod</c>, <c>ctrl</c>, <c>alt</c>, <c>shift</c> and
    ///     <c>meta</c>, joined to the key with <c>+</c>. The field shows it, in the reader's platform's words.
    /// </summary>
    public string? Shortcut { get; set; }

    /// <summary>What the list says when nothing matches. "No results" unless this says otherwise.</summary>
    public string? EmptyText { get; set; }

    public string? Class { get; set; }

    // The scope is filled by the commands as they render, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    private string Prefix => "uicmd-" + _instance.ToString(CultureInfo.InvariantCulture);

    private string DialogId => Prefix + "-dialog";

    private string ListId => Prefix + "-list";

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        Trigger(),
        // The first command that can be picked is lit as it renders; from there the highlight is the runtime's.
        Palette(SearchBox(), new UiMenuScope(Prefix, options: true))
    ];

    private Component Trigger()
    {
        (string, string?)[] opens = [("command", "show-modal"), ("commandfor", DialogId), ("popovertarget", DialogId)];

        return Button
            .Type(ButtonType.Button)
            .Class(UiClass.Compose("input w-full cursor-pointer justify-between gap-2 text-left", Class))
            .Aria("haspopup", "dialog")
            .Attributes(Shortcut is { } shortcut ? [.. opens, ("data-rask-shortcut", shortcut)] : opens)[
            Span.Class("flex min-w-0 items-center gap-2 opacity-70")[
                Ui.Icon.Name(Ui.IconName.MagnifyingGlass).Class("size-4 shrink-0"),
                Span.Class("truncate")[Label]
            ],
            Shortcut is { } keys ? Keys(keys) : null
        ];
    }

    // No value and no handler: what is typed, and the command it leaves lit, are the browser's (rask-command.ts).
    private HTMLInputElement<string> SearchBox() =>
        Input.Of<string>()
            .Type(InputType.Text)
            .Class("grow bg-transparent py-3 outline-none")
            .Placeholder(Placeholder ?? Label)
            .Autocomplete("off")
            .Autofocus()
            .Role("combobox")
            .Aria(("label", Label), ("expanded", "true"), ("controls", ListId), ("autocomplete", "list"))
            // Enter presses the highlighted option in the runtime, because what an option DOES — a handler, a link —
            // is only reachable by clicking it; C# cannot follow an href.
            .Attributes(("data-rask-press-active", null), UiRuntimeMarks.NotFormData);

    private Component Palette(HTMLInputElement<string> box, UiMenuScope scope)
    {
        (string, string?)[] closes =
            [("command", "close"), ("commandfor", DialogId), ("popovertarget", DialogId), ("popovertargetaction", "hide")];

        return Dialog
            .Id(DialogId)
            .Class("modal modal-top sm:modal-middle")
            .Popover(Popover.Auto)
            .Aria("label", Label)
            // A pick closes the palette in the runtime, after the pick's own handler has run; closed, it starts
            // over there too: yesterday's query is a narrowed list nobody asked for.
            .Attributes(("data-rask-command", null), ("data-rask-close-on-pick", null))[
            Div.Class("modal-box max-w-lg p-0")[
                Div.Class("flex items-center gap-2 border-b border-base-300 px-4")[
                    Ui.Icon.Name(Ui.IconName.MagnifyingGlass).Class("size-4 shrink-0 opacity-60"),
                    box
                ],
                Ul.Id(ListId)
                    .Role("listbox")
                    .Aria("label", Label)
                    .Class("ui-command-list menu max-h-80 w-full flex-nowrap overflow-y-auto p-2")[
                    Context.Provide(new UiMenuLevel(scope, -1))[Children ?? []],
                    Li.Class("ui-command-empty px-3 py-6 text-center text-sm opacity-60").Role("presentation")[
                        EmptyText ?? RaskStrings.Get(RaskString.CommandEmpty, "No results")
                    ]
                ]
            ],
            Button
                .Type(ButtonType.Button)
                .Class("modal-backdrop")
                .Aria("hidden", "true")
                .TabIndex(-1)
                .Attributes(closes)["close"]
        ];
    }

    // The shortcut in both platforms' words; the runtime marks a Mac, and the stylesheet shows the one that applies.
    // Both are rendered rather than one chosen in C#, which has no idea what the reader is holding.
    private static Component Keys(string shortcut) =>
        Span.Class("flex shrink-0 gap-1").Aria("hidden", "true")[
            RaskMarkup.Kbd.Class("kbd kbd-sm ui-kbd-mac")[UiShortcut.Describe(shortcut, mac: true)],
            RaskMarkup.Kbd.Class("kbd kbd-sm ui-kbd-other")[UiShortcut.Describe(shortcut, mac: false)]
        ];
}
