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
/// Ui.MenuItem.Href(SignOut.Path).SignOut()
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

    // `.SignOut()`: the catalog's question, and its words and icon where the item has none of its own.
    private bool _signOut;

    /// <summary>
    ///     Asks the reader before the item's pick goes, as <c>.Confirm(…)</c> does on an element:
    ///     <c>Ui.MenuItem.Danger.OnClick(() =&gt; Delete(row)).Confirm("Delete the partner?", heading: "Delete partner")</c>.
    ///     Cancelled, nothing is sent and nothing is followed.
    /// </summary>
    /// <remarks>
    ///     Asked of a pick that runs <see cref="OnClick" />, and of one that follows <see cref="Href" />: a yes then
    ///     follows the link — an in-app route in the app, a string as the browser follows any link, a GET and a page
    ///     load (<c>Ui.MenuItem.Href("/logout").Confirm("Sign out?")</c>). Asked in <c>Ui.Confirm</c> where the layout
    ///     placed one, otherwise in the browser's own dialog. With
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

    /// <summary>
    ///     The item that signs the reader out, asking first in the catalog's words:
    ///     <c>Ui.MenuItem.Href(SignOut.Path).SignOut()</c> asks "Are you sure you want to sign out?" under "Sign out" —
    ///     in Hungarian "Biztos ki szeretne jelentkezni?" under "Kijelentkezés" — and on a yes follows the link.
    /// </summary>
    /// <remarks>
    ///     <see cref="Confirm" /> with <c>RaskString.SignOutQuestion</c> under <c>RaskString.SignOut</c>, and the item's
    ///     words and icon where it has none of its own: "Sign out" and
    ///     <see cref="Ui.IconName.ArrowRightStartOnRectangle" />, as Flux UI's profile menu draws it. It signs no one
    ///     out itself: the <see cref="Href" /> it follows does — an endpoint that answers a GET — or the
    ///     <see cref="OnClick" /> it runs.
    /// </remarks>
    /// <returns>This item.</returns>
    public UiMenuItem SignOut()
    {
        _signOut = true;
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
        var words = Words();
        var icon = Icon ?? (_signOut ? Ui.IconName.ArrowRightStartOnRectangle : null);
        var label = UiMenuRow.Label(words);
        var disabled = Disabled == true;
        var ordinal = level?.Scope.Register(level.Parent, label, disabled, isSub: false) ?? -1;
        var aria = new Dictionary<string, string?>(StringComparer.Ordinal);
        var data = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (icon is not null)
        {
            data["ui-menu-item-has-icon"] = "";
        }

        if (KeepOpen == true)
        {
            data["rask-keep-open"] = "";
        }

        Component?[] content =
        [
            icon is { } leading ? UiMenuRow.Icon(leading, IconVariant) : UiMenuRow.Indent(),
            .. words ?? [],
            Suffix is { } suffix ? Div.Class(UiMenuRow.Trailing)[suffix] : null,
            Kbd is { } kbd ? Div.Class(UiMenuRow.Trailing)[kbd] : null,
            IconTrailing is { } trailing ? UiMenuRow.IconTrailing(trailing, IconVariant) : null
        ];
        var classes = UiClass.Compose(UiMenuRow.Classes(Variant), Class);

        if (Href is { Path: not null } href && !disabled)
        {
            // A STRING is an ordinary link, written as given with no path base added (#1070): the way out of the app.
            // No handler on it — the runtime would take the click, and the browser has to follow this one itself.
            // A question on it is asked by the runtime before the browser follows it, and a yes follows it then.
            if (href.PageType is null)
            {
                var plain = Asking(A.Href(href.ToString()).Class(classes));
                return UiMenuRow.Decorate(plain, level, ordinal, "menuitem", "ui-menu-item", aria, data)[content];
            }

            var link = NavLink.Href(href).ActiveClass("").Class(classes);
            link = Asking(OnClick.HasValue ? link.OnClick(OnClick) : link);

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
    // A `.Confirm(…)` of the item's own wins over `.SignOut()`'s, which is read here so it speaks the render's language.
    private T Asking<T>(T element)
        where T : Element
    {
        if (_confirm is { } ask)
        {
            return element.Confirm(ask.Text, ask.Expect, ask.Heading);
        }

        return _signOut
            ? element.Confirm(
                RaskStrings.Get(RaskString.SignOutQuestion, "Are you sure you want to sign out?"),
                heading: SignOutWords())
            : element;
    }

    // The item's words: its children, or under `.SignOut()` with none, the catalog's.
    private IEnumerable<Component?>? Words() =>
        _signOut && (Children is null || !Children.Any()) ? [(Component)SignOutWords()] : Children;

    private static string SignOutWords() => RaskStrings.Get(RaskString.SignOut, "Sign out");
}
