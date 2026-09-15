namespace Rask.Ui;

/// <summary>
/// A nested menu: a row that opens more rows beside it.
/// </summary>
/// <remarks>
/// <para>
/// Inside a <see cref="UiDropdown" /> it flies out beside its row. A pointer opens it by hovering, and the kit's
/// stylesheet gives the pointer a <b>safe triangle</b>: a wedge from the row to the flyout that still counts as
/// the row, plus a short delay before it closes, so moving diagonally toward the flyout — across the next row
/// down — does not snap it shut. The keyboard opens it with ArrowRight and closes it with ArrowLeft; a tap opens it
/// on a touch screen.
/// </para>
/// <para>
/// Inside a plain <see cref="UiMenu" /> — a navigation list — it is daisyUI's collapsible <c>&lt;details&gt;</c>
/// group instead, which opens in place with no runtime.
/// </para>
/// </remarks>
public sealed partial class UiMenuSub : Component
{
    /// <summary>The row's words.</summary>
    public required string Heading { get; set; }

    public UiIconName? Icon { get; set; }

    /// <summary>Shown but not openable. The keyboard cursor skips it.</summary>
    public bool? Disabled { get; set; }

    public string? Class { get; set; }

    // Registration happens in Render; see UiMenuItem.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        if (Context.Get<UiMenuLevel>() is not { } level)
        {
            return Li.Class(UiClass.Compose(Class))[
                Details[
                    Summary[
                        Icon is { } icon ? UiIcon.Name(icon).Class("size-4 shrink-0") : null,
                        Heading
                    ],
                    Ul[Children ?? []]
                ]
            ];
        }

        var scope = level.Scope;
        var ordinal = scope.Register(level.Parent, Heading, Disabled == true, isSub: true);
        var open = scope.IsOpen(ordinal);
        var subMenuId = scope.ItemId(ordinal) + "-sub";

        var aria = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["haspopup"] = "menu",
            ["expanded"] = open ? "true" : "false",
            ["controls"] = subMenuId,
        };
        if (Disabled == true)
        {
            aria["disabled"] = "true";
        }

        var trigger = Button
            .Type("button")
            .Class(UiClass.Compose(ordinal == scope.Active ? "menu-focus" : "", Disabled == true ? "menu-disabled" : ""));
        if (Disabled != true)
        {
            // A tap has no hover to open it with; this is the touch screen's way in, and a second tap closes it.
            trigger = trigger.OnClick(() => scope.ToggleSub(ordinal));
        }

        trigger = UiMenuItemMarkup.AsMenuItem(trigger, level, ordinal, "menuitem", aria, keepOpen: true, isChecked: false);

        var li = Li.Role("none").Class(UiClass.Compose("ui-menu-sub", Class));
        if (open)
        {
            li = li.Data("open", "");
        }

        return li[
            trigger[global::Rask.Ui.UiMenuItem.Row(Icon, Heading, kbd: null, UiIconName.ChevronRight, indicator: null)],
            Ul
                .Id(subMenuId)
                .Role("menu")
                .Class("menu ui-menu-flyout w-56 rounded-box border border-base-300 bg-base-100 p-2 shadow-sm")
                .Aria(new Dictionary<string, string?> { ["label"] = Heading })[
                Context.Provide(new UiMenuLevel(scope, ordinal))[Children ?? []]
            ]
        ];
    }
}
