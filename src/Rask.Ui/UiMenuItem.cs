namespace Rask;

/// <summary>
/// One entry in a <see cref="UiMenu" /> or a <see cref="UiDropdown" />.
/// </summary>
/// <remarks>
/// <para>
/// Inside a dropdown it is a <c>menuitem</c> the keyboard cursor can land on: it registers with the dropdown as
/// it renders, carries the id the menu's <c>aria-activedescendant</c> names, and marks itself
/// <c>data-highlighted</c> while the cursor is on it. Picking it closes the menu unless <see cref="KeepOpen" />
/// says otherwise. Outside one — a navigation list — it is an ordinary link or button, with no menu roles.
/// </para>
/// <para>
/// <c>data-highlighted</c>, not Flux's <c>data-active</c>, because <see cref="Active" /> already means "the page
/// being shown" here.
/// </para>
/// </remarks>
public sealed partial class UiMenuItem : Component
{
    public new required string Text { get; set; }

    public string? Href { get; set; }

    public Ui.IconName? Icon { get; set; }

    /// <summary>An icon at the end of the row.</summary>
    public Ui.IconName? IconTrailing { get; set; }

    /// <summary>
    ///     A keyboard shortcut shown at the end of the row — <c>"⌘S"</c>. Display only: it teaches the shortcut,
    ///     the app still binds it.
    /// </summary>
    public string? Kbd { get; set; }

    /// <summary><see cref="Ui.Tone.Error" /> for a destructive item — Flux's <c>variant="danger"</c>.</summary>
    public Ui.Tone? Tone { get; set; }

    /// <summary>Shown but not pickable. The keyboard cursor skips it.</summary>
    public bool? Disabled { get; set; }

    /// <summary>Keeps the dropdown open after this item is picked.</summary>
    public bool? KeepOpen { get; set; }

    /// <summary>The page this entry leads to is the page being shown. Writes <c>aria-current="page"</c>.</summary>
    public bool? Active { get; set; }

    public Callback OnClick { get; set; }

    public string? Class { get; set; }

    // Registration happens in Render, so a cached render would drop out of the dropdown's cursor.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var level = Context.Get<UiMenuLevel>();
        if (level?.Scope.Hides(Text) == true)
        {
            // Not rendered and not registered, so the keyboard cursor can never land on a command out of sight.
            return null;
        }

        var ordinal = level?.Scope.Register(level.Parent, Text, Disabled == true, isSub: false) ?? -1;
        var content = Row(Icon, Text, Kbd, IconTrailing, indicator: null);

        Component inner;
        if (Href is { } href && Disabled != true)
        {
            var link = A.Href(href).Class(ItemClass(level, ordinal));
            inner = Decorate(link, level, ordinal)[content];
        }
        else
        {
            var button = Button.Type("button").Class(ItemClass(level, ordinal)).Disabled(Disabled == true && level is null);
            if (Disabled != true)
            {
                button = button.OnClick(OnClick);
            }

            inner = Decorate(button, level, ordinal)[content];
        }

        return Li.Class(Class).Role(level is null ? null : "none")[inner];
    }

    private string ItemClass(UiMenuLevel? level, int ordinal) =>
        UiClass.Compose(
            Active == true ? "menu-active" : "",
            level is not null && ordinal == level.Scope.Active ? "menu-focus" : "",
            Tone == Ui.Tone.Error ? "text-error" : "",
            Disabled == true ? "menu-disabled" : "");

    private T Decorate<T>(T element, UiMenuLevel? level, int ordinal)
        where T : Element
    {
        var aria = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (Active == true)
        {
            aria["current"] = "page";
        }

        if (level is null)
        {
            return aria.Count > 0 ? element.Aria(aria) : element;
        }

        if (Disabled == true)
        {
            aria["disabled"] = "true";
        }

        if (level.Scope.AsOptions)
        {
            // An option says whether it is the highlighted one; focus stays in the palette's search box.
            aria["selected"] = ordinal == level.Scope.Active ? "true" : "false";
            return UiMenuItemMarkup.AsMenuItem(element, level, ordinal, "option", aria, KeepOpen == true, isChecked: false);
        }

        return UiMenuItemMarkup.AsMenuItem(element, level, ordinal, "menuitem", aria, KeepOpen == true, isChecked: false);
    }

    // Shared by every kind of item, and reached from the others as `global::Rask.UiMenuItem.Row`: inside a
    // markup host the bare type name is the chain entry, not the type.

    /// <summary>The inside of a row: indicator, icon, words, shortcut, trailing icon.</summary>
    internal static Component Row(Ui.IconName? icon, string text, string? kbd, Ui.IconName? trailing, Component? indicator) =>
        [
            indicator,
            icon is { } leading ? Ui.Icon.Name(leading).Class("size-4 shrink-0") : null,
            Span.Class("grow")[text],
            kbd is null ? null : RaskMarkup.Kbd.Class("kbd kbd-xs ui-menu-kbd")[kbd],
            trailing is { } end ? Ui.Icon.Name(end).Class("size-4 shrink-0 opacity-60") : null
        ];

    /// <summary>A check or radio row's mark. Always the same width, checked or not, so every row's words line up.</summary>
    internal static Component Indicator(bool on) =>
        Span.Class("inline-flex size-4 shrink-0 items-center justify-center").Aria("hidden", "true")[
            on ? Ui.Icon.Name(Ui.IconName.Check).Class("size-4") : null
        ];
}

/// <summary>What makes a row a menu item inside a dropdown, for every kind of item.</summary>
internal static class UiMenuItemMarkup
{
    /// <summary>
    ///     Makes <paramref name="element" /> the menu item at <paramref name="ordinal" />: its id, role, tab stop,
    ///     ARIA, and the <c>data-*</c> hooks for the cursor, a pick that keeps the menu open, and a checked row.
    /// </summary>
    internal static T AsMenuItem<T>(
        T element,
        UiMenuLevel? level,
        int ordinal,
        string role,
        Dictionary<string, string?> aria,
        bool keepOpen,
        bool isChecked)
        where T : Element
    {
        var data = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (isChecked)
        {
            data["checked"] = "";
        }

        // Chain steps, not property writes: a chain-built element renders what its chain was given, and a later
        // assignment to the property is invisible to it (RASK045 catches the non-generic case).
        if (level is null)
        {
            element = element.Role(role).Aria(aria);
            return data.Count > 0 ? element.Data(data) : element;
        }

        var scope = level.Scope;
        element = element.Id(scope.ItemId(ordinal)).Role(role).TabIndex(-1).Aria(aria);

        if (ordinal == scope.Active)
        {
            // Flux's styling hook for the row under the cursor, named for what it is.
            data["highlighted"] = "";
        }

        if (keepOpen)
        {
            data["rask-keep-open"] = "";
        }

        return data.Count > 0 ? element.Data(data) : element;
    }
}
