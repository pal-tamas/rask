using System.Globalization;

namespace Rask;

/// <summary>
/// A menu that opens where the reader right-clicks. Flux UI's <c>flux:context</c>.
/// </summary>
/// <remarks>
/// <para>
/// The first child is the area that is right-clicked — a card, a row, a canvas — and the second is the
/// <see cref="UiMenu" /> that opens, the same menu a <see cref="UiDropdown" /> takes.
/// </para>
/// <code>
/// Ui.Context[
///     Ui.Card["Right click"],
///     Ui.Menu[
///         Ui.MenuItem.Icon(Ui.IconName.Plus)["New post"],
///         Ui.MenuItem.Danger.Icon(Ui.IconName.Trash)["Delete"]
///     ]
/// ]
/// </code>
/// <para>
/// The opening is the RUNTIME's, not a round trip: a right-click on the area shows the popover at the pointer
/// straight away, on either host, and C# hears about it through the popover's toggle like any other menu. The
/// ContextMenu key and Shift+F10 open it too, at the focused element — so give the area something focusable if
/// the menu holds anything a keyboard user needs.
/// </para>
/// <para>
/// Nothing in it should be the only way to do something. iOS Safari never fires a context-menu event, so a
/// context menu is a shortcut to actions that also live somewhere visible.
/// </para>
/// </remarks>
public sealed partial class UiContext : Component
{
    // Per instance, so two context menus on one page cannot collide on the id the right-click hook names.
    private static int _instances;

    private readonly int _instance = Interlocked.Increment(ref _instances);

    private bool _open;

    /// <summary>Where the menu opens against the pointer. Below it, the menu's end edge on it, unless this says otherwise.</summary>
    public Ui.ContextPosition? Position { get; set; }

    /// <summary>The distance between the pointer and the menu, in pixels.</summary>
    public int? Gap { get; set; }

    /// <summary>Moves the menu by this much more, along both axes, in pixels.</summary>
    public (int X, int Y)? Offset { get; set; }

    /// <summary>
    ///     The <see cref="UiMenu.Id" /> of a menu written somewhere else, for when it cannot sit inside this
    ///     element. Without it the menu is the second child.
    /// </summary>
    public string? Target { get; set; }

    /// <summary>A value for the menu's <c>data-detail</c>, to style or read by which area opened it.</summary>
    public string? Detail { get; set; }

    /// <summary>A right-click opens the browser's own menu, as if this were not here.</summary>
    public bool? Disabled { get; set; }

    /// <summary>
    ///     Whether the menu is open. Leave it unset to let the reader open and close it; set it to take
    ///     ownership, and pair it with <see cref="OnToggle" />.
    /// </summary>
    public bool? Open { get; set; }

    /// <summary>Runs when the reader opens or closes it, with the state it is now in.</summary>
    public Callback<bool> OnToggle { get; set; }

    public string? Class { get; set; }

    // _open is a FIELD, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    private string PanelId => "uictx-" + _instance.ToString(CultureInfo.InvariantCulture) + "-panel";

    /// <inheritdoc />
    protected override Component? Render()
    {
        var parts = (Children ?? []).Where(child => child is not null).ToList();
        var host = new UiPopupHost(PanelId, PanelStyle(), Open, Detail, ToggledAsync);

        var data = new Dictionary<string, string?>(StringComparer.Ordinal) { ["ui-context"] = "" };
        if (Disabled != true)
        {
            // The runtime's hook: a right-click inside this opens the popover with that id, at the pointer.
            data["rask-contextmenu"] = Target ?? PanelId;
        }

        if (Open ?? _open)
        {
            // Flux's `data-open`: an area that looks selected while its menu is up.
            data["open"] = "";
        }

        return Div.Class(UiClass.Compose("inline", Class)).Data(data)[
            parts.Count > 0 ? parts[0] : null,
            Context.Provide(host)[parts.Skip(1)]
        ];
    }

    private async Task ToggledAsync(bool open)
    {
        _open = open;

        // Controlled: tell the page only about a change it did not make itself.
        if (open != Open)
        {
            await OnToggle.Invoke(open).ConfigureAwait(false);
        }
    }

    // The runtime sets --rask-context-x/-y on the root (in a sheet of its own) as it opens the menu — not on
    // the menu, whose style attribute a render rewrites. Which of the menu's corners lands there is a translate:
    // "end" puts the menu's end edge on the pointer, so the menu reaches back from it.
    private string PanelStyle() => PointerStyle(Position ?? Ui.ContextPosition.BottomEnd, Gap, Offset ?? (0, 0));

    /// <summary>The style that puts a menu at the pointer. A menu named by <see cref="Target" /> uses the default.</summary>
    internal static string PointerStyle(Ui.ContextPosition position, int? distance, (int X, int Y) offset)
    {
        var (x, y) = offset;
        // As on Ui.Dropdown: Flux's reference says 4, and its page draws 5.
        var gap = distance ?? 5;
        var across = position switch
        {
            Ui.ContextPosition.BottomStart or Ui.ContextPosition.TopStart => "0%",
            Ui.ContextPosition.BottomCenter or Ui.ContextPosition.TopCenter => "-50%",
            _ => "-100%",
        };
        var above = position is Ui.ContextPosition.TopEnd or Ui.ContextPosition.TopCenter or Ui.ContextPosition.TopStart;

        return "position:fixed;inset:auto;left:var(--rask-context-x,0px);top:var(--rask-context-y,0px);margin:0"
               + ";translate:calc(" + across + " + " + x.ToString(CultureInfo.InvariantCulture) + "px) calc("
               + (above ? "-100% - " : "0% + ") + gap.ToString(CultureInfo.InvariantCulture) + "px + "
               + y.ToString(CultureInfo.InvariantCulture) + "px)";
    }
}
