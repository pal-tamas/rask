namespace Rask;

/// <summary>
/// A row that opens more rows beside it. Flux UI's <c>flux:menu.submenu</c>.
/// </summary>
/// <remarks>
/// <para>
/// A pointer opens it by resting on the row, at once, and the runtime gives the pointer Flux's safe area
/// (<c>data-rask-safe-area</c>): the triangle from the pointer to the flyout still counts as the row, so moving
/// diagonally toward the flyout — across the next row down — does not close it. It stays open after the pointer
/// has left the menu, and closes when another row is entered. The keyboard opens it with ArrowRight or Enter
/// and closes it with ArrowLeft; a tap opens it on a touch screen. All of it happens in the browser: an open
/// flyout is the runtime's <c>data-open</c> on this element, not the page's state.
/// </para>
/// <code>
/// Ui.MenuSubmenu.Heading("Sort by")[
///     Ui.MenuRadioGroup.Bind(() =&gt; view.Sort)[
///         Ui.MenuRadio.Value("name")["Name"],
///         Ui.MenuRadio.Value("date")["Date"]
///     ]
/// ]
/// </code>
/// </remarks>
public sealed partial class UiMenuSubmenu : Component
{
    /// <summary>The row's words.</summary>
    public required string Heading { get; set; }

    /// <summary>An icon at the start of the row.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>The icon at the end of the row. A chevron unless this says otherwise.</summary>
    public Ui.IconName? IconTrailing { get; set; }

    /// <summary>Which drawing of the icons to use. The 20px <see cref="Ui.IconVariant.Mini" /> unless this says otherwise.</summary>
    public Ui.IconVariant? IconVariant { get; set; }

    /// <summary>Keeps the menu open after any row in this submenu is picked.</summary>
    public bool? KeepOpen { get; set; }

    public string? Class { get; set; }

    // Registration happens in Render; see Ui.MenuItem.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var level = Context.Get<UiMenuLevel>();
        var scope = level?.Scope;
        var ordinal = scope?.Register(level!.Parent, Heading, disabled: false, isSub: true) ?? -1;

        // No ARIA of its own, as Flux's submenu row has none: a `menuitem` beside the `menu` it opens.
        var aria = new Dictionary<string, string?>(StringComparer.Ordinal);
        // No value: the flyout is the element right after the row, as in Flux, and needs no id (rask-menu.ts).
        var data = new Dictionary<string, string?>(StringComparer.Ordinal) { ["rask-safe-area"] = "" };
        if (Icon is not null)
        {
            data["ui-menu-item-has-icon"] = "";
        }

        var flyout = Div.Role("menu").TabIndex(-1).Class(UiMenuRow.Panel);

        // No handler: a tap opens the flyout and the next closes it in the browser, as the pointer and the keys do.
        var row = Button.Type(ButtonType.Button).Class(UiClass.Compose(UiMenuRow.Classes(variant: null), Class));

        return Div.Class("inline").Data(Submenu)[
            UiMenuRow.Decorate(row, level, ordinal, "menuitem", "ui-menu-item", aria, data)[
                Icon is { } icon ? UiMenuRow.Icon(icon, IconVariant) : UiMenuRow.Indent(),
                Heading,
                IconTrailing is { } trailing ? UiMenuRow.IconTrailing(trailing, IconVariant) : UiMenuRow.Chevrons()
            ],
            flyout.Data(KeepOpen == true ? KeepsOpen : Flyout)[
                scope is null ? [.. Children ?? []] : Context.Provide(new UiMenuLevel(scope, ordinal))[Children ?? []]
            ]
        ];
    }

    // A menu of its own to the pointer: its rows are lit apart from the rows of the menu it flies out of.
    private static readonly Dictionary<string, string?> Flyout =
        new(StringComparer.Ordinal) { ["ui-menu"] = "", ["rask-menu-pointer"] = "" };

    private static readonly Dictionary<string, string?> KeepsOpen =
        new(StringComparer.Ordinal) { ["ui-menu"] = "", ["rask-menu-pointer"] = "", ["rask-keep-open"] = "" };

    // The runtime writes `data-open` beside this while the flyout is open, which is what the stylesheet shows it
    // by once the pointer is no longer on it.
    private static readonly Dictionary<string, string?> Submenu = new(StringComparer.Ordinal) { ["ui-menu-submenu"] = "" };
}
