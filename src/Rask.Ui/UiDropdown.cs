using System.Globalization;

namespace Rask;

/// <summary>
/// A trigger and the menu it opens beside it. Flux UI's <c>flux:dropdown</c>.
/// </summary>
/// <remarks>
/// <para>
/// The first child is the trigger — a <c>Button</c>, a <see cref="UiButton" />, any element that is a button —
/// and the second is what it opens: a <see cref="UiMenu" /> of actions, or a <see cref="UiNavmenu" /> of links.
/// </para>
/// <code>
/// Ui.Dropdown[
///     Ui.Button.IconTrailing(Ui.IconName.ChevronDown)["Options"],
///     Ui.Menu[
///         Ui.MenuItem.Icon(Ui.IconName.Plus).OnClick(New)["New post"],
///         Ui.MenuSeparator,
///         Ui.MenuItem.Danger.Icon(Ui.IconName.Trash).OnClick(Delete)["Delete"]
///     ]
/// ]
/// </code>
/// <para>
/// The panel is a <c>[popover]</c> the trigger opens with <c>popovertarget</c>, so the browser owns opening and
/// closing it — the top layer, Escape, a click outside, focus handed back to the trigger — and it opens with no
/// runtime at all. It is placed with CSS anchor positioning: <see cref="Position" /> picks the side,
/// <see cref="Align" /> slides it along that side, <see cref="Gap" /> and <see cref="Offset" /> nudge it.
/// </para>
/// <para>
/// <see cref="Open" /> unset, the reader opens and closes it and the page is not asked. Set, the page owns it:
/// the runtime shows or hides the popover to match, and <see cref="OnToggle" /> is how the page hears the reader.
/// </para>
/// </remarks>
public sealed partial class UiDropdown : Component
{
    // Per instance, so two dropdowns on one page cannot collide on the id that joins a trigger to its panel.
    private static int _instances;

    private readonly int _instance = Interlocked.Increment(ref _instances);

    private bool _open;

    /// <summary>Which side of the trigger the menu opens on. Below, unless this says otherwise.</summary>
    public Ui.DropdownPosition? Position { get; set; }

    /// <summary>Where along that side the menu sits. Its start edge on the trigger's, unless this says otherwise.</summary>
    public Ui.DropdownAlign? Align { get; set; }

    /// <summary>Shifts the menu along its alignment, in pixels. Negative moves it back past the trigger's edge.</summary>
    public int? Offset { get; set; }

    /// <summary>The distance between the trigger and the menu, in pixels.</summary>
    public int? Gap { get; set; }

    /// <summary>
    ///     Whether the menu is open. Leave it unset to let the reader open and close it; set it to take
    ///     ownership, and pair it with <see cref="OnToggle" />.
    /// </summary>
    public bool? Open { get; set; }

    /// <summary>Runs when the reader opens or closes it, with the state it is now in.</summary>
    public Callback<bool> OnToggle { get; set; }

    /// <summary>
    ///     Opens the menu while the pointer is over the trigger or the menu, and closes it over neither. A
    ///     press and the keyboard still open it, and nothing hovers on a touch screen.
    /// </summary>
    public bool? Hover { get; set; }

    public string? Class { get; set; }

    // What a part of the kit that draws a dropdown of its own adds to it: a marker beside the dropdown's, and
    // the selector the root matches while a hover may open it.
    private (string Marker, string HoverIf)? _owner;

    /// <summary>
    ///     Makes this the dropdown of another part: marked as that part's, and opened by a hover only while the
    ///     root matches <paramref name="hoverIf" />. The kit's own chain step.
    /// </summary>
    internal UiDropdown OwnedBy(string marker, string hoverIf)
    {
        _owner = (marker, hoverIf);
        // What a generated step does when it writes a new value: the dropdown is drawn again with it.
        BuilderRuntime.MarkChanged(this);

        return this;
    }

    // _open is a FIELD, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    private string Prefix => "uidd-" + _instance.ToString(CultureInfo.InvariantCulture);

    private string PanelId => Prefix + "-panel";

    /// <summary>
    ///     The anchor the menu is placed against. The dropdown's own box carries it; a trigger that carries it
    ///     too is the later of the two in the tree, and so the one the menu follows.
    /// </summary>
    internal string AnchorName => "--" + Prefix;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var open = Open ?? _open;
        var parts = (Children ?? []).Where(child => child is not null).ToList();
        var host = new UiPopupHost(PanelId, PanelStyle(), Open, Detail: null, ToggledAsync);

        var root = Div
            // A part that owns the dropdown says how it lays out: its `hidden` must not meet a display of ours.
            .Class(UiClass.Compose(_owner is null ? "inline-flex" : "", Class))
            .Style("anchor-name:--" + Prefix)
            .Data(Marks(open));

        return root[
            parts.Count > 0 ? Trigger(parts[0]!, open) : null,
            Context.Provide(host)[parts.Skip(1)]
        ];
    }

    private static readonly Dictionary<string, string?> Closed = new(StringComparer.Ordinal) { ["ui-dropdown"] = "" };

    private static readonly Dictionary<string, string?> Opened = new(StringComparer.Ordinal) { ["ui-dropdown"] = "", ["open"] = "" };

    // Flux's marker and its `data-open`, and — for one that opens on hover — the runtime's hook: the root wraps
    // the trigger and the panel, so "over the root" is over either.
    private Dictionary<string, string?> Marks(bool open)
    {
        if (Hover != true && _owner is null)
        {
            return open ? Opened : Closed;
        }

        var marks = new Dictionary<string, string?>(open ? Opened : Closed, StringComparer.Ordinal);
        if (Hover == true)
        {
            marks["rask-hover"] = PanelId;
        }

        if (_owner is { } owner)
        {
            marks[owner.Marker] = "";
            marks["rask-hover-if"] = owner.HoverIf;
        }

        return marks;
    }

    // What makes the caller's element the trigger. It is THEIR element, so what they put on it is kept.
    // `aria-haspopup="true"` whatever it opens, a menu or a list of links: that is what Flux writes.
    // A trigger of the kit's own that is not an element (a profile, an item of a nav bar) wires the button it draws.
    private Component Trigger(Component trigger, bool open) => trigger switch
    {
        Element element => UiInvoker.Decorate(element, PanelId, "true", open),
        IUiTrigger own => own.Invoking(PanelId, open),
        _ => trigger,
    };

    private async Task ToggledAsync(bool open)
    {
        _open = open;

        // Controlled: tell the page only about a change it did not make itself — the runtime showing the popover
        // because Open became true fires this same event.
        if (open != Open)
        {
            await OnToggle.Invoke(open).ConfigureAwait(false);
        }
    }

    // Placement by CSS anchor positioning, in a style attribute: nothing here is scanned by Tailwind, so the
    // values are built freely. Each edge is an inset measured from the trigger's — the gap on the edge that
    // faces it, the offset on the edge the menu is aligned to — so both follow the menu when it flips to the side
    // that has room, and a closed menu carries no margin of its own.
    private string PanelStyle()
    {
        var position = Position ?? Ui.DropdownPosition.Bottom;
        var beside = position is Ui.DropdownPosition.Left or Ui.DropdownPosition.Right;
        // Flux's reference says 4; its page draws 5 when the prop is not written, and 4 when it is written as 4.
        var gap = (Gap ?? 5).ToString(CultureInfo.InvariantCulture) + "px";
        var offset = (Offset ?? 0).ToString(CultureInfo.InvariantCulture) + "px";
        var facing = position switch
        {
            Ui.DropdownPosition.Top => "inset-block-end:calc(anchor(start) + " + gap + ")",
            Ui.DropdownPosition.Left => "inset-inline-end:calc(anchor(start) + " + gap + ")",
            Ui.DropdownPosition.Right => "inset-inline-start:calc(anchor(end) + " + gap + ")",
            _ => "inset-block-start:calc(anchor(end) + " + gap + ")",
        };
        var axis = beside ? "inset-block" : "inset-inline";
        var along = (Align ?? Ui.DropdownAlign.Start) switch
        {
            Ui.DropdownAlign.Center => axis + "-start:calc(anchor(center) + " + offset + ");translate:" + (beside ? "0 -50%" : "-50% 0"),
            Ui.DropdownAlign.End => axis + "-end:calc(anchor(end) + " + offset + ")",
            _ => axis + "-start:calc(anchor(start) + " + offset + ")",
        };

        return "position-anchor:--" + Prefix
               + ";inset:auto;margin:0;" + facing + ";" + along
               + ";position-try-fallbacks:" + (beside ? "flip-inline" : "flip-block");
    }
}
