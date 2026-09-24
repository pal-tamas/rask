namespace Rask;

/// <summary>A row of tabs: links to views with URLs, or — inside a <see cref="UiTabGroup" /> — the tablist.</summary>
/// <remarks>
/// <para>
/// daisyUI's <c>tabs</c>, and <b>links by default</b>. A tab that is a URL is bookmarkable, survives a
/// refresh, answers the back button and works before the runtime boots; a tab that is state in a field is none
/// of those, so that is what a <see cref="UiTab" /> with an <c>Href</c> stays.
/// </para>
/// <para>
/// Put this row inside a <see cref="UiTabGroup" /> and it becomes the <c>tablist</c> for the panels beside it,
/// and it takes the keyboard with it: ArrowLeft/ArrowRight move and show, Home and End jump to the ends. That
/// is for views that genuinely have no URL — a detail pane beside a record — where an address would be
/// inventing state the page does not have.
/// </para>
/// <para>
/// Scrolls rather than wraps: these carry counts that change as a queue drains, and a wrapping row
/// would change height underneath an operator mid-read.
/// </para>
/// </remarks>
public sealed partial class UiTabs : Component
{
    /// <summary>How the row is drawn.</summary>
    public Ui.TabStyle? Style { get; set; }

    public Ui.Size? Size { get; set; }

    /// <summary>
    ///     Which side of its panel the row sits on. Only <see cref="Ui.Position.Top" /> and
    ///     <see cref="Ui.Position.Bottom" /> mean anything here; anything else draws the default.
    /// </summary>
    public Ui.Position? Position { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        // Inside a Ui.TabGroup this row IS the tablist the panels hang off, and it owns the keyboard. Outside
        // one it is a row of links, which the browser already moves between with Tab.
        //
        // The handler goes on before the children indexer, which is what keeps the receiver an Element: past the
        // indexer it is a Component, and an element step has nothing left to attach to.
        var row = TabRow();
        if (Context.Get<UiTabScope>() is { } scope)
        {
            row = row.OnKeyDown(e => OnKeyAsync(e, scope));
        }

        return row[Children ?? []];
    }

    // The tabs pattern, activating as it moves: ArrowLeft/Right step and SHOW, Home and End jump to the ends.
    // Wraps, because a tab row is a ring — there is no "past the last tab" for a reader to fall off.
    private static Task OnKeyAsync(Rask.Core.Live.KeyboardEventArgs e, UiTabScope scope)
    {
        var names = scope.Names;
        if (names.Count == 0 || e.Ctrl || e.Alt || e.Meta)
        {
            return Task.CompletedTask;
        }

        var at = scope.Selected is { } current ? IndexOf(names, current) : -1;
        var next = e.Key switch
        {
            "ArrowRight" or "ArrowDown" => at < 0 ? 0 : (at + 1) % names.Count,
            "ArrowLeft" or "ArrowUp" => at < 0 ? names.Count - 1 : (at - 1 + names.Count) % names.Count,
            "Home" => 0,
            "End" => names.Count - 1,
            _ => -1,
        };

        return next < 0 ? Task.CompletedTask : scope.Select(names[next]);
    }

    private static int IndexOf(IReadOnlyList<string> names, string name)
    {
        for (var i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private Element TabRow() =>
        // role=tablist on a <nav> of links: daisyUI's own markup for the link form, and what tells
        // assistive technology these are alternatives rather than an arbitrary run of links.
        Nav.Role("tablist")
            .Class(UiClass.Compose(
                "tabs",
                Style is { } style ? UiClassNames.TabsStyle(style) : "",
                Size is { } size ? UiClassNames.TabsSize(size) : "",
                Position is { } position ? UiClassNames.TabsPosition(position) : "",
                // No outer margin: the kit styles, the page spaces. The phone bleed to the screen edge that
                // used to live here (`-mx-3 px-3`) is a property of where a row sits, so it is the call
                // site's to add — a row inside a card had it bleed out of the card.
                "flex-nowrap overflow-x-auto sm:flex-wrap "
                + "[-ms-overflow-style:none] [scrollbar-width:none] [&::-webkit-scrollbar]:hidden",
                Class));
}

