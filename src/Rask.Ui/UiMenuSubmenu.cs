namespace Rask;

/// <summary>
/// A row that opens more rows beside it. Flux UI's <c>flux:menu.submenu</c>.
/// </summary>
/// <remarks>
/// <para>
/// A pointer opens it by resting on the row, at once, and the kit's stylesheet gives the pointer a safe
/// triangle: a wedge from the row to the flyout that still counts as the row, so moving diagonally toward the
/// flyout — across the next row down — does not close it. The keyboard opens it with ArrowRight or Enter and
/// closes it with ArrowLeft; a tap opens it on a touch screen.
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
        var open = scope?.IsOpen(ordinal) == true;

        // No ARIA of its own, as Flux's submenu row has none: a `menuitem` beside the `menu` it opens.
        var aria = new Dictionary<string, string?>(StringComparer.Ordinal);
        var data = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (Icon is not null)
        {
            data["ui-menu-item-has-icon"] = "";
        }

        var flyout = Div.Role("menu").TabIndex(-1).Class(UiMenuRow.Panel);

        var row = Button.Type(ButtonType.Button).Class(UiClass.Compose(UiMenuRow.Classes(variant: null), Class));
        if (scope is not null)
        {
            // A tap has no hover to open it with; this is the touch screen's way in, and a second tap closes it.
            row = row.OnClick(() => scope.ToggleSub(ordinal));
        }

        return Div.Class("inline").Data(Markers(open))[
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

    private static readonly Dictionary<string, string?> Flyout = new(StringComparer.Ordinal) { ["ui-menu"] = "" };

    private static readonly Dictionary<string, string?> KeepsOpen =
        new(StringComparer.Ordinal) { ["ui-menu"] = "", ["rask-keep-open"] = "" };

    // `data-open` is what the stylesheet shows the flyout by when the keyboard or a tap opened it.
    private static Dictionary<string, string?> Markers(bool open)
    {
        var data = new Dictionary<string, string?>(StringComparer.Ordinal) { ["ui-menu-submenu"] = "" };
        if (open)
        {
            data["open"] = "";
        }

        return data;
    }
}
