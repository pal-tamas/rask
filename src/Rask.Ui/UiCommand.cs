using System.Globalization;
using Rask.Core.Live;

namespace Rask.Ui;

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
/// Typing narrows the list in C#, case- and accent-insensitively in the reader's culture, the way
/// <c>UiSelect.Searchable</c> does; a command that does not match is not rendered at all, so the keyboard never
/// lands on one that is out of sight. Separators are dropped while there is a query, since what they separated has
/// been filtered.
/// </para>
/// </remarks>
public sealed partial class UiCommand : Component
{
    private static int _instances;

    private readonly int _instance = Interlocked.Increment(ref _instances);
    private string _query = string.Empty;
    private int _cursor;
    private UiMenuScope? _scope;

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

    // The query, the cursor and the scope are FIELDS, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    private string Prefix => "uicmd-" + _instance.ToString(CultureInfo.InvariantCulture);

    private string DialogId => Prefix + "-dialog";

    private string ListId => Prefix + "-list";

    /// <inheritdoc />
    protected override Component? Render()
    {
        // The items register while THIS render walks its children, after this method returns — so where the cursor
        // lands is worked out against the list the last render left, which is the list the reader has been looking
        // at. After typing, the cursor is back at 0, which every narrowed list starts with.
        var active = _scope is { } previous ? Active(previous.Entries) : 0;
        _scope = new UiMenuScope(
            Prefix,
            active,
            new HashSet<int>(),
            keepOpen: false,
            static _ => Task.CompletedTask,
            query: _query,
            options: true);

        (string, string?)[] opens = [("command", "show-modal"), ("commandfor", DialogId), ("popovertarget", DialogId)];
        (string, string?)[] closes =
            [("command", "close"), ("commandfor", DialogId), ("popovertarget", DialogId), ("popovertargetaction", "hide")];

        var trigger = Button
            .Type("button")
            .Class(UiClass.Compose("input w-full cursor-pointer justify-between gap-2 text-left", Class))
            .Aria(new Dictionary<string, string?> { ["haspopup"] = "dialog" })
            .Attributes(Shortcut is { } shortcut ? [.. opens, ("data-rask-shortcut", shortcut)] : opens);

        var box = Input
            .Value(_query)
            .Type(InputType.Text)
            .Class("grow bg-transparent py-3 outline-none")
            .Placeholder(Placeholder ?? Label)
            .Autocomplete("off")
            .Autofocus(true)
            .Role("combobox")
            .Aria(BoxAria(active))
            // Enter presses the highlighted option in the runtime, because what an option DOES — a handler, a link —
            // is only reachable by clicking it; C# cannot follow an href.
            .Attributes(("data-rask-press-active", null))
            .OnInput(raw =>
            {
                _query = raw ?? string.Empty;
                // Back to the top of the narrowed list.
                _cursor = 0;
            })
            .OnKeyDown(OnKey);

        var dialog = Dialog
            .Id(DialogId)
            .Class("modal modal-top sm:modal-middle")
            .Popover("auto")
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            // A pick closes the palette in the runtime, after the pick's own handler has run.
            .Attributes(("data-rask-close-on-pick", null))
            .OnToggle(e =>
            {
                if (!e.IsOpen)
                {
                    // Reopened, it starts over: yesterday's query is a narrowed list nobody asked for.
                    _query = string.Empty;
                    _cursor = 0;
                }
            });

        return
        [
            trigger[
                Span.Class("flex min-w-0 items-center gap-2 opacity-70")[
                    UiIcon.Name(UiIconName.Search).Class("size-4 shrink-0"),
                    Span.Class("truncate")[Label]
                ],
                Shortcut is { } keys ? Keys(keys) : null
            ],
            dialog[
                Div.Class("modal-box max-w-lg p-0")[
                    Div.Class("flex items-center gap-2 border-b border-base-300 px-4")[
                        UiIcon.Name(UiIconName.Search).Class("size-4 shrink-0 opacity-60"),
                        box
                    ],
                    Ul.Id(ListId)
                        .Role("listbox")
                        .Aria(new Dictionary<string, string?> { ["label"] = Label })
                        .Class("ui-command-list menu max-h-80 w-full flex-nowrap overflow-y-auto p-2")[
                        Context.Provide(new UiMenuLevel(_scope, -1))[Children ?? []],
                        Li.Class("ui-command-empty px-3 py-6 text-center text-sm opacity-60").Role("presentation")[
                            EmptyText ?? "No results"
                        ]
                    ]
                ],
                Button
                    .Type("button")
                    .Class("modal-backdrop")
                    .Aria(new Dictionary<string, string?> { ["hidden"] = "true" })
                    .TabIndex(-1)
                    .Attributes(closes)["close"]
            ]
        ];
    }

    private Dictionary<string, string?> BoxAria(int active)
    {
        var aria = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["label"] = Label,
            // The list is always shown while the dialog is, so the box always says so — which is also what turns on
            // the runtime's containment of the arrow keys and Enter.
            ["expanded"] = "true",
            ["controls"] = ListId,
            ["autocomplete"] = "list",
        };

        if (active >= 0 && _scope is { } scope)
        {
            aria["activedescendant"] = scope.ItemId(active);
        }

        return aria;
    }

    // The cursor as the last render left the list: clamped to what is there, and off anything that cannot be picked.
    private int Active(IReadOnlyList<UiMenuEntry> entries)
    {
        if (entries.Count == 0)
        {
            return -1;
        }

        var at = Math.Clamp(_cursor, 0, entries.Count - 1);
        return entries[at].Disabled
            ? UiSelectNav.Step(at, +1, entries.Count, i => entries[i].Disabled)
            : at;
    }

    private void OnKey(KeyboardEventArgs e)
    {
        if (_scope is not { } scope || scope.Entries.Count == 0 || e.Ctrl || e.Alt || e.Meta)
        {
            return;
        }

        var entries = scope.Entries;
        var at = Active(entries);
        var dir = e.Key switch
        {
            "ArrowDown" => +1,
            "ArrowUp" => -1,
            _ => 0,
        };
        if (dir == 0)
        {
            return;
        }

        var next = at < 0 ? -1 : UiSelectNav.Step(at, dir, entries.Count, i => entries[i].Disabled);
        // A palette wraps, as a menu does.
        if (next < 0 || next == at)
        {
            next = dir > 0
                ? UiSelectNav.FirstEnabled(entries.Count, i => entries[i].Disabled)
                : UiSelectNav.LastEnabled(entries.Count, i => entries[i].Disabled);
        }

        _cursor = next < 0 ? at : next;
    }

    // The shortcut in both platforms' words; the runtime marks a Mac, and the stylesheet shows the one that applies.
    // Both are rendered rather than one chosen in C#, which has no idea what the reader is holding.
    private static Component Keys(string shortcut) =>
        Span.Class("flex shrink-0 gap-1").Aria("hidden", "true")[
            RaskMarkup.Kbd.Class("kbd kbd-sm ui-kbd-mac")[UiShortcut.Describe(shortcut, mac: true)],
            RaskMarkup.Kbd.Class("kbd kbd-sm ui-kbd-other")[UiShortcut.Describe(shortcut, mac: false)]
        ];
}
