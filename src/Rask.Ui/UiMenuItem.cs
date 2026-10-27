using Rask.Core.Routing;

namespace Rask;

/// <summary>
/// One action in a <see cref="UiMenu" />. Flux UI's <c>flux:menu.item</c>.
/// </summary>
/// <remarks>
/// <para>
/// Its children are its words. It is a <c>menuitem</c> the keyboard cursor can land on, and acts the Rask way:
/// <see cref="OnClick" /> runs a handler, <see cref="Href" /> follows a link. Picking it closes the menu unless
/// it, or the menu, says <see cref="KeepOpen" />.
/// </para>
/// <code>
/// Ui.MenuItem.Icon(Ui.IconName.PencilSquare).Kbd("⌘S").OnClick(Save)["Save"]
/// Ui.MenuItem.Danger.Icon(Ui.IconName.Trash).OnClick(Delete).Confirm("Delete the partner?")["Delete"]
/// </code>
/// </remarks>
public sealed partial class UiMenuItem : Component
{
    /// <summary>An icon at the start of the row.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>An icon at the end of the row.</summary>
    public Ui.IconName? IconTrailing { get; set; }

    /// <summary>Which drawing of the icons to use. The 20px <see cref="Ui.IconVariant.Mini" /> unless this says otherwise.</summary>
    public Ui.IconVariant? IconVariant { get; set; }

    /// <summary>
    ///     A keyboard shortcut shown at the end of the row — <c>"⌘S"</c>. Display only: it teaches the shortcut,
    ///     the app still binds it.
    /// </summary>
    public string? Kbd { get; set; }

    /// <summary>Words at the end of the row — a count, a state.</summary>
    public string? Suffix { get; set; }

    /// <summary><see cref="Ui.MenuItemVariant.Danger" /> for a destructive action.</summary>
    public Ui.MenuItemVariant? Variant { get; set; }

    /// <summary>Shown but not pickable. The keyboard cursor steps over it.</summary>
    public bool? Disabled { get; set; }

    /// <summary>Keeps the menu open after this item is picked.</summary>
    public bool? KeepOpen { get; set; }

    /// <summary>Where the item leads. In-app navigation for a generated route; an ordinary link for a string.</summary>
    public RouteUrl? Href { get; set; }

    public Callback OnClick { get; set; }

    public string? Class { get; set; }

    // What `.Confirm(…)` asks, put on the row it draws: a component, the item cannot take the element's own step.
    private (string Text, string? Expect, string? Heading)? _confirm;

    /// <summary>
    ///     Asks the reader before the item's pick is sent, as <c>.Confirm(…)</c> does on an element:
    ///     <c>Ui.MenuItem.Danger.OnClick(() =&gt; Delete(row)).Confirm("Delete the partner?", heading: "Delete partner")</c>.
    ///     Cancelled, nothing is sent.
    /// </summary>
    /// <remarks>
    ///     Asked of a pick that runs <see cref="OnClick" />: a link to a string, which the browser follows by itself,
    ///     sends nothing to hold back. Asked in <c>Ui.Confirm</c> where the layout placed one, otherwise in the
    ///     browser's own dialog. With
    ///     <paramref name="expect" /> the reader types the answer, and only an exact match goes: a guard against a slip,
    ///     never a secret, as it is written into the page.
    /// </remarks>
    /// <param name="text">The question.</param>
    /// <param name="expect">What the reader must type; null asks yes or no.</param>
    /// <param name="heading">The title over the question in <c>Ui.Confirm</c>; null keeps the layout's.</param>
    /// <returns>This item.</returns>
    public UiMenuItem Confirm(string text, string? expect = null, string? heading = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        _confirm = (text, expect, heading);
        // What a generated step does when it writes a new value: the item is drawn again with it.
        BuilderRuntime.MarkChanged(this);
        return this;
    }

    // Registration happens in Render, so a cached render would drop out of the menu's cursor.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var level = Context.Get<UiMenuLevel>();
        var label = UiMenuRow.Label(Children);
        var disabled = Disabled == true;
        var ordinal = level?.Scope.Register(level.Parent, label, disabled, isSub: false) ?? -1;
        var aria = new Dictionary<string, string?>(StringComparer.Ordinal);
        var data = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (Icon is not null)
        {
            data["ui-menu-item-has-icon"] = "";
        }

        if (KeepOpen == true)
        {
            data["rask-keep-open"] = "";
        }

        Component?[] content =
        [
            Icon is { } icon ? UiMenuRow.Icon(icon, IconVariant) : UiMenuRow.Indent(),
            .. Children ?? [],
            Suffix is { } suffix ? Div.Class(UiMenuRow.Trailing)[suffix] : null,
            Kbd is { } kbd ? Div.Class(UiMenuRow.Trailing)[kbd] : null,
            IconTrailing is { } trailing ? UiMenuRow.IconTrailing(trailing, IconVariant) : null
        ];
        var classes = UiClass.Compose(UiMenuRow.Classes(Variant), Class);

        if (Href is { Path: not null } href && !disabled)
        {
            // A STRING is an ordinary link, written as given with no path base added (#1070): the way out of the app.
            // No handler on it — the runtime would take the click, and the browser has to follow this one itself.
            if (href.PageType is null)
            {
                var plain = A.Href(href.ToString()).Class(classes);
                return UiMenuRow.Decorate(plain, level, ordinal, "menuitem", "ui-menu-item", aria, data)[content];
            }

            var link = NavLink.Href(href).ActiveClass("").Class(classes);
            if (OnClick.HasValue)
            {
                link = Asking(link.OnClick(OnClick));
            }

            return UiMenuRow.Decorate(link, level, ordinal, "menuitem", "ui-menu-item", aria, data)[content];
        }

        var button = Button.Type(ButtonType.Button).Class(classes).Disabled(disabled);
        // Nothing to run, nothing to send: the runtime closes the menu on a pick by itself.
        if (!disabled && OnClick.HasValue)
        {
            button = Asking(button.OnClick(OnClick));
        }

        return UiMenuRow.Decorate(button, level, ordinal, "menuitem", "ui-menu-item", aria, data)[content];
    }

    // The question, on the element whose click is sent: the runtime asks it before the click leaves the browser.
    private T Asking<T>(T element)
        where T : Element =>
        _confirm is { } ask ? element.Confirm(ask.Text, ask.Expect, ask.Heading) : element;
}
